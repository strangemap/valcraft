package dev.rehan.passthrough.client.mixin;

import net.minecraft.client.MouseHandler;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

@Mixin(MouseHandler.class)
public interface MouseHandlerAccessor {
	@Accessor("xpos")
	void passthrough$setXpos(double x);

	@Accessor("ypos")
	void passthrough$setYpos(double y);
}
