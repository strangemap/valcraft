package dev.rehan.passthrough.client;

import dev.rehan.passthrough.Passthrough;
import java.lang.foreign.Arena;
import java.lang.foreign.FunctionDescriptor;
import java.lang.foreign.Linker;
import java.lang.foreign.MemorySegment;
import java.lang.foreign.SymbolLookup;
import java.lang.foreign.ValueLayout;
import java.lang.invoke.MethodHandle;
import net.minecraft.client.Minecraft;
import org.lwjgl.sdl.SDLVideo;

/**
 * Who has the keyboard and mouse. Normally the host's window; while a Minecraft screen is open (inventory, chat,
 * crafting) Minecraft's own window, which lies invisible exactly over the host's picture, is raised so it gets the
 * clicks; when the screen closes the host gets the focus back (Minecraft, being in front, is allowed to give it away).
 */
final class HostWindow {
	/** The host's top-level window (HWND), sent with "view". */
	static volatile long hwnd;
	private static MethodHandle setForegroundWindow;

	private HostWindow() {
	}

	static void raiseMinecraft(final Minecraft minecraft) {
		long handle = minecraft.getWindow().handle();
		SDLVideo.SDL_RaiseWindow(handle);
	}

	static void focusHost() {
		if (hwnd == 0L) {
			return;
		}

		try {
			if (setForegroundWindow == null) {
				SymbolLookup user32 = SymbolLookup.libraryLookup("user32", Arena.global());
				setForegroundWindow = Linker.nativeLinker().downcallHandle(user32.find("SetForegroundWindow").orElseThrow(),
					FunctionDescriptor.of(ValueLayout.JAVA_INT, ValueLayout.ADDRESS));
			}

			int ok = (int) setForegroundWindow.invoke(MemorySegment.ofAddress(hwnd));
			if (ok == 0) {
				Passthrough.LOG.warn("couldn't give the focus back to the host window");
			}
		} catch (Throwable t) {
			Passthrough.LOG.warn("focusHost failed", t);
		}
	}
}
