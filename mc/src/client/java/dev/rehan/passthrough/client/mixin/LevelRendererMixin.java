package dev.rehan.passthrough.client.mixin;

import com.mojang.blaze3d.vertex.PoseStack;
import dev.rehan.passthrough.client.HostState;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.LevelRenderer;
import net.minecraft.client.renderer.SubmitNodeCollector;
import net.minecraft.client.renderer.state.level.LevelRenderState;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.phys.BlockHitResult;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** The host's ground is invisible barriers here: no black outline around the host's rocks and hills when aimed at. */
@Mixin(LevelRenderer.class)
abstract class LevelRendererMixin {
	@Inject(method = "submitBlockOutline", at = @At("HEAD"), cancellable = true)
	private void passthrough$noBarrierOutline(final PoseStack poseStack, final SubmitNodeCollector collector, final LevelRenderState state, final CallbackInfo ci) {
		Minecraft minecraft = Minecraft.getInstance();
		if (HostState.frame() != null && minecraft.level != null && minecraft.hitResult instanceof BlockHitResult hit
			&& minecraft.level.getBlockState(hit.getBlockPos()).is(Blocks.BARRIER)) {
			ci.cancel();
		}
	}
}
