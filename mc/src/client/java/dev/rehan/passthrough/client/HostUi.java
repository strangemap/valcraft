package dev.rehan.passthrough.client;

import com.google.gson.JsonObject;
import com.mojang.blaze3d.platform.Window;
import dev.rehan.passthrough.client.mixin.MouseHandlerAccessor;
import net.minecraft.client.Minecraft;
import net.minecraft.client.MouseHandler;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.CharacterEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.MouseButtonInfo;

/**
 * Minecraft's screens (inventory, chat, ...) worked from the host's window: the host keeps the focus (a fullscreen game
 * would drop out of fullscreen otherwise) and forwards the mouse and keys here.
 * {"t":"ui","x":0..1,"y":0..1 (top-left), "b":1 left|2 middle|3 right,"down":bool, "wheel":d, "key":SDL scancode, "char":codepoint, "shift":bool}
 */
final class HostUi {
	private static int held = -1;
	private static double lastX, lastY;

	private HostUi() {
	}

	static void handle(final Minecraft minecraft, final JsonObject m) {
		Screen screen = minecraft.gui.screen();
		if (screen == null) {
			return;
		}

		Window window = minecraft.getWindow();
		int mods = m.has("shift") && m.get("shift").getAsBoolean() ? 3 : 0;
		if (m.has("x")) {
			// the cursor, in window pixels (hover and tooltips read it) and in GUI units
			double px = m.get("x").getAsDouble() * window.getScreenWidth();
			double py = m.get("y").getAsDouble() * window.getScreenHeight();
			MouseHandlerAccessor mouse = (MouseHandlerAccessor) minecraft.mouseHandler;
			mouse.passthrough$setXpos(px);
			mouse.passthrough$setYpos(py);
			double gx = MouseHandler.getScaledXPos(window, px), gy = MouseHandler.getScaledYPos(window, py);
			if (m.has("b")) {
				int b = m.get("b").getAsInt();
				MouseButtonEvent event = new MouseButtonEvent(gx, gy, new MouseButtonInfo(b, mods));
				if (m.get("down").getAsBoolean()) {
					held = b;
					screen.mouseClicked(event, false);
				} else {
					held = -1;
					screen.mouseReleased(event);
				}
			} else if (m.has("wheel")) {
				screen.mouseScrolled(gx, gy, 0.0, m.get("wheel").getAsDouble());
			} else {
				screen.mouseMoved(gx, gy);
				if (held >= 0) {
					screen.mouseDragged(new MouseButtonEvent(gx, gy, new MouseButtonInfo(held, mods)), gx - lastX, gy - lastY);
				}
			}
			lastX = gx;
			lastY = gy;
		}

		if (m.has("key")) {
			KeyEvent event = new KeyEvent(m.get("key").getAsInt(), 0, mods);
			if (m.get("down").getAsBoolean()) {
				screen.keyPressed(event);
			} else {
				screen.keyReleased(event);
			}
		}

		if (m.has("char")) {
			screen.charTyped(new CharacterEvent(m.get("char").getAsInt()));
		}
	}
}
