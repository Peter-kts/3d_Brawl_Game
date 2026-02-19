# 3D Brawl Game

Unity 3D brawler project. Use the same Unity version on every machine so scenes and assets open correctly.

## Unity version

**6000.3.4f1** (Unity 6)

Install this exact version from [Unity Hub](https://unity.com/download) or the archive.

## Get the project on another machine

1. **Clone the repo** (use branch `nonlockon`):
   ```bash
   git clone --branch nonlockon https://github.com/Peter-kts/3d_Brawl_Game.git
   cd 3d_Brawl_Game
   ```
   Or with SSH: `git clone --branch nonlockon git@github.com:Peter-kts/3d_Brawl_Game.git`

2. **Pull LFS assets** (if not automatic):
   ```bash
   git lfs pull
   ```

3. **Open in Unity**  
   In Unity Hub: Add → select the `3d_Brawl_Game` folder. Use editor version **6000.3.4f1**.  
   Let Unity import and regenerate the `Library` folder (first open can take a few minutes).

4. **Work as usual**  
   Commit and push from this machine; pull on the other. Don’t copy the `Library` folder between machines.
