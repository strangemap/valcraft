using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ValCraft
{
	/// <summary>
	/// The WebSocket to Minecraft's passthrough mod (ws://127.0.0.1:25599). Keeps reconnecting; sends and receives
	/// on background threads, so the game thread only enqueues and polls.
	/// </summary>
	public sealed class Link
	{
		private readonly Uri uri;
		private readonly ConcurrentQueue<string> outbox = new ConcurrentQueue<string>();
		private readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
		private readonly AutoResetEvent wake = new AutoResetEvent(false);
		private volatile bool connected;
		private volatile int generation;
		private volatile bool stopping;

		public Link(string url)
		{
			uri = new Uri(url);
		}

		public bool Connected => connected;
		/// <summary>Bumped on every new connection (the plugin re-sends its setup then).</summary>
		public int Generation => generation;

		public void Start()
		{
			var t = new Thread(Run) { IsBackground = true, Name = "ValCraft link" };
			t.Start();
		}

		public void Stop()
		{
			stopping = true;
			wake.Set();
		}

		public void Send(string message)
		{
			if (!connected)
				return;
			// a stuck Minecraft must not grow this without bound (camera messages are only useful fresh)
			if (outbox.Count > 512)
				return;
			outbox.Enqueue(message);
			wake.Set();
		}

		private string camMessage;

		/// <summary>The camera: only the newest one is worth sending (older ones would make Minecraft lag behind).</summary>
		public void SendCam(string message)
		{
			if (!connected)
				return;
			Interlocked.Exchange(ref camMessage, message);
			wake.Set();
		}

		public bool Poll(out string message) => inbox.TryDequeue(out message);

		private void Run()
		{
			while (!stopping)
			{
				using (var ws = new ClientWebSocket())
				{
					try
					{
						ws.ConnectAsync(uri, CancellationToken.None).Wait(3000);
						if (ws.State != WebSocketState.Open)
						{
							Thread.Sleep(1000);
							continue;
						}
						while (outbox.TryDequeue(out _))
						{
						}
						connected = true;
						generation++;
						Plugin.Log("connected to Minecraft");
						var reader = Task.Run(() => Receive(ws));
						while (!stopping && ws.State == WebSocketState.Open && !reader.IsCompleted)
						{
							wake.WaitOne(20);
							var cam = Interlocked.Exchange(ref camMessage, null);
							if (cam != null)
							{
								var cb = Encoding.UTF8.GetBytes(cam);
								ws.SendAsync(new ArraySegment<byte>(cb), WebSocketMessageType.Text, true, CancellationToken.None).Wait();
							}
							while (outbox.TryDequeue(out var m))
							{
								var bytes = Encoding.UTF8.GetBytes(m);
								ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).Wait();
							}
						}
					}
					catch (Exception e)
					{
						if (connected)
							Plugin.Log("link lost: " + e.GetBaseException().Message);
					}
					finally
					{
						if (connected)
							Plugin.Log("disconnected from Minecraft");
						connected = false;
					}
				}
				Thread.Sleep(1000);
			}
		}

		private void Receive(ClientWebSocket ws)
		{
			var buffer = new byte[1 << 16];
			var sb = new StringBuilder();
			try
			{
				while (ws.State == WebSocketState.Open)
				{
					var r = ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).Result;
					if (r.MessageType == WebSocketMessageType.Close)
						return;
					sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
					if (r.EndOfMessage)
					{
						if (inbox.Count < 4096)
							inbox.Enqueue(sb.ToString());
						sb.Clear();
					}
				}
			}
			catch (Exception)
			{
			}
		}
	}
}
