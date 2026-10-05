package dev.rehan.passthrough.client.mixin;

import dev.rehan.passthrough.client.HostState;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** In-game screens (inventory, chat) over the host's picture: no darkening or blur behind them, the host shows through. */
@Mixin(Screen.class)
abstract class ScreenMixin {
	private static boolean passthrough$overHost() {
		return HostState.frame() != null && Minecraft.getInstance().level != null;
	}

	@Inject(method = "extractBlurredBackground", at = @At("HEAD"), cancellable = true)
	private void passthrough$noBlur(final GuiGraphicsExtractor graphics, final CallbackInfo ci) {
		if (passthrough$overHost()) {
			ci.cancel();
		}
	}

	@Inject(method = "extractTransparentBackground", at = @At("HEAD"), cancellable = true)
	private void passthrough$noDarkening(final GuiGraphicsExtractor graphics, final CallbackInfo ci) {
		if (passthrough$overHost()) {
			ci.cancel();
		}
	}

	@Inject(method = "extractMenuBackground(Lnet/minecraft/client/gui/GuiGraphicsExtractor;)V", at = @At("HEAD"), cancellable = true)
	private void passthrough$noMenuBackground(final GuiGraphicsExtractor graphics, final CallbackInfo ci) {
		if (passthrough$overHost()) {
			ci.cancel();
		}
	}
}
