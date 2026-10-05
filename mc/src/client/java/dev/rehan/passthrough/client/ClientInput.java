package dev.rehan.passthrough.client;

import com.google.gson.JsonObject;
import dev.rehan.passthrough.Passthrough;
import dev.rehan.passthrough.client.mixin.KeyMappingAccessor;
import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.KeyMapping;
import net.minecraft.client.gui.screens.debug.GameModeSwitcherScreen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.world.entity.player.Inventory;
import org.lwjgl.sdl.SDLVideo;

/** Host input, applied on the client thread: the host window has the focus, so Minecraft never sees these itself. */
final class ClientInput {
	private ClientInput() {
	}

	static void handle(final Minecraft minecraft, final JsonObject m) {
		LocalPlayer player = minecraft.player;
		switch (m.get("t").getAsString()) {
			case "key" -> {
				String k = m.get("k").getAsString();
				boolean down = !m.has("down") || m.get("down").getAsBoolean();
				if (k.equals("escape")) {
					if (down && minecraft.gui.screen() != null) {
						minecraft.gui.screen().onClose();
					}

					return;
				}

				KeyMapping key = switch (k) {
					case "use" -> minecraft.options.keyUse;
					case "attack" -> minecraft.options.keyAttack;
					case "pick" -> minecraft.options.keyPickItem;
					case "inventory" -> minecraft.options.keyInventory;
					case "drop" -> minecraft.options.keyDrop;
					case "swap" -> minecraft.options.keySwapOffhand;
					default -> null;
				};
				if (k.equals("attack") && down && player != null) {
					// a swing: the host hits what's in front of Steve in its own world, by what he holds (its name and
					// Minecraft's attack damage with it)
					String item = BuiltInRegistries.ITEM.getKey(player.getMainHandItem().getItem()).getPath();
					double damage = player.getAttributeValue(net.minecraft.world.entity.ai.attributes.Attributes.ATTACK_DAMAGE);
					Passthrough.events.accept(String.format(java.util.Locale.ROOT, "{\"t\":\"melee\",\"item\":\"%s\",\"dmg\":%.2f}", item, damage));
				}

				if (key != null) {
					if (down && !key.isDown()) {
						KeyMappingAccessor access = (KeyMappingAccessor)key;
						access.passthrough$setClickCount(access.passthrough$getClickCount() + 1);
					}

					key.setDown(down);
				}
			}
			case "gms" -> {
				// F3+F4 held in the host's window: Minecraft's game mode switcher, shown over the host's picture and
				// driven from there ("open", "next" on each F4, "apply" when F3 is let go)
				String op = m.get("op").getAsString();
				if (op.equals("open") && !(minecraft.gui.screen() instanceof GameModeSwitcherScreen)) {
					minecraft.gui.setScreen(new GameModeSwitcherScreen());
				} else if (minecraft.gui.screen() instanceof GameModeSwitcherScreen screen) {
					if (op.equals("next")) {
						screen.keyPressed(new KeyEvent(InputConstants.KEY_F4, 0, 0));
					} else if (op.equals("apply")) {
						screen.keyReleased(new KeyEvent(InputConstants.KEY_F3, 0, 0));
					}
				}
			}
			case "ui" -> HostUi.handle(minecraft, m);
			case "quit" -> minecraft.stop(); // the host closed: Minecraft goes with it (the world saves on the way out)
			case "slot" -> {
				if (player != null) {
					player.getInventory().setSelectedSlot(Math.clamp(m.get("n").getAsInt(), 0, Inventory.getSelectionSize() - 1));
				}
			}
			case "scroll" -> {
				if (player != null) {
					Inventory inventory = player.getInventory();
					int size = Inventory.getSelectionSize();
					inventory.setSelectedSlot(Math.floorMod(inventory.getSelectedSlot() - m.get("d").getAsInt(), size));
				}
			}
			case "hud" -> {
				if (minecraft.gui.hud.isHidden() != m.get("hidden").getAsBoolean()) {
					minecraft.gui.hud.toggle();
				}
			}
			case "view" -> {
				// match the host's picture exactly: un-minimize/un-maximize first (resizing a maximized window is ignored)
				int w = m.get("w").getAsInt(), h = m.get("h").getAsInt();
				long handle = minecraft.getWindow().handle();
				SDLVideo.SDL_RestoreWindow(handle);
				minecraft.getWindow().setWindowed(w, h);
				SDLVideo.SDL_SetWindowSize(handle, w, h);
				SDLVideo.SDL_SyncWindow(handle);
				if (m.has("x")) {
					// a borderless, see-through window exactly over the host's picture: invisible, but Minecraft's
					// screens (inventory, chat) take the mouse there when it is raised (see PassthroughClient)
					SDLVideo.SDL_SetWindowBordered(handle, false);
					SDLVideo.SDL_SetWindowPosition(handle, m.get("x").getAsInt(), m.get("y").getAsInt());
					SDLVideo.SDL_SetWindowOpacity(handle, 0.01F);
				}

				if (m.has("hwnd")) {
					HostWindow.hwnd = m.get("hwnd").getAsLong();
				}
			}
			default -> {
			}
		}
	}
}
