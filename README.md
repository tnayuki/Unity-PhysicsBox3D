# Unity-PhysicsBox3D

A Unity binding for [Box3D](https://github.com/erincatto/box3d) — Erin Catto's new open-source 3D physics engine — benchmarked side by side against Unity's built-in PhysX.

The sync layer follows the batched transform write-back design of Unity's official Physics Core 2D (the Box2D v3 binding): moved bodies are fetched from native code in a single call into a contiguous buffer, then written back to Transforms in parallel with `IJobParallelForTransform`. No per-body P/Invoke.

## Results

Measured on an Apple Silicon Mac (4P+6E cores), falling-tower scenario, 500-step average right after spawn, matched materials and depenetration speed on both sides:

- **Sleep enabled (game-like, includes settling):** Box3D is 1.6–1.9x faster than PhysX (4096 bodies: 2.12 ms vs 3.92 ms). Most of the gap comes from convergence quality — Box3D (TGS soft) puts a settled pile to sleep within seconds, while PhysX (PGS) keeps ~98% of bodies awake from contact jitter.
- **Sleep disabled (pure full-load solver):** near parity — 1.07–1.08x at 1024–4096 bodies, PhysX ahead at 512.

Box3D's multithreading matters: single-threaded it loses at 4096 bodies (14.3 ms vs 6.6 ms). This binding enables Box3D's built-in scheduler (`workerCount > 1`, no task system required) with an adaptive worker count of `clamp(bodies / 128, 1, cores)`.

## Setup

macOS Editor only (the native plugin is built as a universal dylib).

```sh
git clone --recursive https://github.com/tnayuki/Unity-PhysicsBox3D.git
cd Unity-PhysicsBox3D
./native/build_macos.sh   # clang only, no CMake → Assets/Plugins/Box3D/box3d.dylib
```

(Already cloned without `--recursive`? Run `git submodule update --init` to fetch Box3D.)

Then open the project in Unity 6.3 (6000.3+).

## Scenes

- **`Assets/Scenes/Benchmark.unity`** — PhysX on the left, Box3D on the right, per-step ms in the HUD. `R` respawns, `↑`/`↓` doubles/halves the body count. An IMGUI toggle disables sleeping for pure solver comparison.
- **`Assets/Scenes/ComponentComparison.unity`** — the same tower and falling spheres on both engines for visual behavior comparison.

Both scenes can be regenerated via *Tools > Box3D > Build Comparison Scenes*.

## Components

- **`Box3DSimulation`** — one per scene; owns the world, steps it, and writes transforms back. Exposes `workerCount`.
- **`Box3DBody`** — Box or Sphere shape, Static or Dynamic.

## License

This project is released into the public domain under the [Unlicense](UNLICENSE). Box3D itself (referenced as a git submodule, not part of this repository) is MIT-licensed, Copyright (c) 2026 Erin Catto.
