////////////////////////////////////////////////////////////////////////////////////////////
                       Hyper Casual FX Pack Vol.1 (by. Kyeoms)
////////////////////////////////////////////////////////////////////////////////////////////

Thank you for purchasing the Hyper Casual FX Pack Vol.1.
This note describes how this package is configured, how texture should be used, and how it works within a Particle System.

This effect is designed to work in a Built-in, URP or HRRP.
To use in Built-in, you have to install "Shader Graph" from package manager and also your project version must be 2021.2.0 or higher.

I put all the effect elements into one textures so that each prefab uses the least amount of material.
But in [1.4] Optional version I separated materials and textures to reduce memory issues.

The structure of the texture is as follows.

   ▷ Red channel is main texture.
   ▷ Green channel is dissolve texture. The main texture gradually dissolve into the shape of green texture.
   ▷ Blue channel is for secondary color.
   ▷ And Alpha channel.

These effects can be modified by two Custom Data in the Particle System.

There are 4 Components in Custom Data 1.

   ▷ X value is for Dissolve. From 0 to 1, it gradually dissolves.
   ▷ Y value is for Dissolve Sharpness. The larger the number, the sharper the edges of dissolve.
   ▷ Z value is for Emissive Power. The larger the number, the stronger emission.
   ▷ W value is for Soft Particle Factor. The larger the number, the more transparent the mesh and overlapping particles become.

You can use Custom Data 2 to add Secondary colors.
If you don't want to use the Secondary colors, change the custom data 2 mode to 'Disabled'.

Material and shader named "VFX_lab" are not used for effects. It was used in the background of Scene just to show the effect.

If you use an Orthographic Camera, and if your project environment is 2D or 2D Experimental and VR, turn off the bool parameter called "Use SoftParticle Factor?" in all materials.

Thank you once again, and I hope my effect will be useful for your development.
- Kyeoms