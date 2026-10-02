# Source control

Repository: https://github.com/soyebmohammad03-dev/NEON-RIFT (public, default branch `main`).

## Git LFS

Install [Git LFS](https://git-lfs.com) and run `git lfs install` before cloning or committing. `.gitattributes` routes models (FBX/GLB/OBJ/blend), textures, audio, EXR reflection probes, screenshots and the generated district meshes (`Assets/_NeonRift/Art/District/Meshes/*.asset`) through LFS. Unity YAML is text with `merge=unityyamlmerge`.

To merge Unity scenes and prefabs, register Unity's merge tool once per clone:

```
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver "'<Unity.app>/Contents/Tools/UnityYAMLMerge' merge -p %O %B %A %A"
```

## Never committed

`Library/`, `Temp/`, `Obj/`, `Build*/`, `Logs/`, `UserSettings/`, IDE solution files, crash reports, the old gameplay recording, the stray `My project/` folder and `SourceArchives/` (the original downloaded archives; the usable extracted content lives in `Assets/ThirdParty/`). See `.gitignore`.

## Authorship

All commits are authored by Soyeb Mohammad (GitHub `soyebmohammad03-dev`).

History note: five early commits (`be0ec0b`, `9753088`, `9e77107`, `3d5ae39`, `de7bbb8`) carry a `Co-Authored-By: Claude` trailer added by the AI coding tool used during development, and three of them use a local machine e-mail address. The history was published unchanged on purpose, because rewriting it would change every commit hash. Commits from `62ebb89` onward carry no such trailer.
