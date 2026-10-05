using System.Collections.Generic;
using UnityEngine;

namespace ValCraft
{
	/// <summary>
	/// What may hide Minecraft: only Valheim's solid things. Every collider near the player (tree trunks, fallen logs,
	/// rocks, the ground, buildings, creatures) gets an invisible copy of its shape on a layer of our own, and a second
	/// camera, matched to Valheim's every frame, renders just those into a depth texture that the add-on tests
	/// Minecraft against. Grass, leaves and bushes have no colliders, so they never hide a block.
	/// </summary>
	internal sealed class Occluders
	{
		private const float Radius = 90f;
		private const float RescanEvery = 0.5f;

		private int layer = -1;
		private int scanMask;
		private Camera cam;
		private RenderTexture depth;
		private Material material;
		private readonly Dictionary<Collider, GameObject> proxies = new Dictionary<Collider, GameObject>();
		private readonly HashSet<Collider> seen = new HashSet<Collider>();
		private readonly List<Collider> gone = new List<Collider>();
		private readonly Dictionary<PrimitiveType, Mesh> primitives = new Dictionary<PrimitiveType, Mesh>();
		private float nextScan;

		public RenderTexture Depth => depth;

		private bool Init()
		{
			if (layer >= 0)
				return true;
			for (int l = 31; l > 8; l--)
				if (string.IsNullOrEmpty(LayerMask.LayerToName(l)))
				{
					layer = l;
					break;
				}
			if (layer < 0)
				return false;
			scanMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain", "vehicle",
				"character", "character_net", "character_ghost", "character_noenv");
			var shader = Shader.Find("Hidden/Internal-Colored");
			material = new Material(shader);
			material.SetInt("_ZWrite", 1);
			material.SetInt("_Cull", 0);
			material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
			var go = new GameObject("ValCraft occluder camera");
			Object.DontDestroyOnLoad(go);
			cam = go.AddComponent<Camera>();
			cam.enabled = true;
			Plugin.Log($"occluders on layer {layer} ({(shader != null ? shader.name : "no shader")})");
			return true;
		}

		/// <summary>Right after Valheim's camera is placed for the frame: match it, and keep the proxies up to date.</summary>
		public void Frame(Camera main, Transform skip, Transform mcBlocks)
		{
			if (!Init())
				return;
			// Valheim's camera never draws the proxies
			main.cullingMask &= ~(1 << layer);
			int w = main.pixelWidth, h = main.pixelHeight;
			if (depth == null || depth.width != w || depth.height != h)
			{
				if (depth != null)
					depth.Release();
				depth = new RenderTexture(w, h, 24, RenderTextureFormat.Depth) { name = "ValCraft occluder depth" };
				depth.Create();
			}
			cam.CopyFrom(main);
			cam.cullingMask = 1 << layer;
			cam.clearFlags = CameraClearFlags.SolidColor;
			cam.backgroundColor = Color.black;
			cam.renderingPath = RenderingPath.Forward;
			cam.allowHDR = false;
			cam.allowMSAA = false;
			cam.targetTexture = depth;
			cam.depth = main.depth - 1;

			if (Time.time >= nextScan)
			{
				nextScan = Time.time + RescanEvery;
				Scan(main.transform.position, skip, mcBlocks);
			}
		}

		private void Scan(Vector3 at, Transform skip, Transform mcBlocks)
		{
			seen.Clear();
			foreach (var c in Physics.OverlapSphere(at, Radius, scanMask, QueryTriggerInteraction.Ignore))
			{
				if (c == null || !c.enabled)
					continue;
				if ((skip != null && c.transform.IsChildOf(skip)) || (mcBlocks != null && c.transform.IsChildOf(mcBlocks)))
					continue;
				seen.Add(c);
				if (!proxies.ContainsKey(c))
					proxies[c] = Make(c);
			}
			gone.Clear();
			foreach (var kv in proxies)
				if (kv.Key == null || !seen.Contains(kv.Key))
					gone.Add(kv.Key);
			foreach (var c in gone)
			{
				if (proxies[c] != null)
					Object.Destroy(proxies[c]);
				proxies.Remove(c);
			}
		}

		private Mesh Primitive(PrimitiveType t)
		{
			if (!primitives.TryGetValue(t, out var m))
			{
				var tmp = GameObject.CreatePrimitive(t);
				m = tmp.GetComponent<MeshFilter>().sharedMesh;
				Object.Destroy(tmp);
				primitives[t] = m;
			}
			return m;
		}

		/// <summary>An invisible copy of the collider's shape (Unity renders it into the texture upside down on D3D: the effect flips it back), a child of it (so it moves with it and goes with it).</summary>
		private GameObject Make(Collider c)
		{
			Mesh mesh = null;
			Vector3 center = Vector3.zero, scale = Vector3.one;
			Quaternion rot = Quaternion.identity;
			switch (c)
			{
				case MeshCollider mc:
					mesh = mc.sharedMesh;
					break;
				case BoxCollider bc:
					mesh = Primitive(PrimitiveType.Cube);
					center = bc.center;
					scale = bc.size;
					break;
				case SphereCollider sc:
					mesh = Primitive(PrimitiveType.Sphere);
					center = sc.center;
					scale = Vector3.one * sc.radius * 2f;
					break;
				case CapsuleCollider cc:
					// a capsule as a cylinder-ish capsule mesh: Unity's is 2 high, 1 wide, along Y
					mesh = Primitive(PrimitiveType.Capsule);
					center = cc.center;
					float d = cc.radius * 2f;
					scale = new Vector3(d, Mathf.Max(cc.height, d) / 2f, d);
					rot = cc.direction == 0 ? Quaternion.Euler(0, 0, 90) : cc.direction == 2 ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
					break;
			}
			if (mesh == null)
				return null;
			var go = new GameObject("vc occluder") { layer = layer };
			go.transform.SetParent(c.transform, false);
			go.transform.localPosition = center;
			go.transform.localRotation = rot;
			go.transform.localScale = scale;
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			var r = go.AddComponent<MeshRenderer>();
			r.sharedMaterial = material;
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			r.receiveShadows = false;
			return go;
		}
	}
}
