# Unity-PhysicsBox3D

A Unity binding for [Box3D](https://github.com/erincatto/box3d) — Erin Catto's new open-source 3D physics engine — benchmarked side by side against Unity's built-in PhysX.

The sync layer follows the batched transform write-back design of Unity's official Physics Core 2D (the Box2D v3 binding): moved bodies are fetched from native code in a single call into a contiguous buffer, then written back to Transforms in parallel with a Burst-compiled `IJobParallelForTransform`. No per-body P/Invoke.

## Results

Measured on an Apple Silicon Mac (4P+6E cores), falling-tower scenario, 500-step average right after spawn, matched materials and depenetration speed on both sides (per-step ms, PhysX / Box3D step+sync):

| bodies | sleep enabled (game-like) | sleep disabled (full-load solver) |
|-------:|--------------------------:|----------------------------------:|
|    512 | 0.500 / **0.294** (1.70x) |  0.859 / **0.778** (1.10x) |
|   1024 | 0.973 / **0.577** (1.69x) |  1.314 / **1.021** (1.29x) |
|   2048 | 1.945 / **0.930** (2.09x) |  2.598 / **2.012** (1.29x) |
|   4096 | 4.658 / **1.930** (2.41x) |  6.016 / **4.229** (1.42x) |

- **Sleep enabled:** Box3D is 1.7–2.4x faster. Most of the gap comes from convergence quality — Box3D (TGS soft) puts a settled pile to sleep within seconds, while PhysX (PGS) keeps ~98% of bodies awake from contact jitter.
- **Sleep disabled:** Box3D ahead across the board (1.1–1.4x).

Box3D's multithreading matters: single-threaded it loses at 4096 bodies (14.3 ms vs 6.6 ms). This binding plugs Box3D's task callbacks into Unity's C# Job System (`Box3DJobBridge`), so the solver runs wide on Unity's existing job workers and Box3D spawns no threads of its own — total thread count stays at the worker pool (~core count), and physics tasks show up in the profiler alongside every other job. The solve fans out with an adaptive worker count of `clamp(bodies / 128, 1, cores)`; correctness never depends on the pool, since the step thread runs worker 0 inline and can complete the whole step alone even if Unity's workers are busy.

Burst-compiling the write-back job is worth 17–29% of the whole step+sync (same-session A/B, sleep enabled: 4096 bodies 2.73 ms → 1.93 ms). The per-body skip check reads each Transform's position and rotation, and that math is what Burst vectorizes away.

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
