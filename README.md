# Unity-PhysicsBox3D

A Unity binding for [Box3D](https://github.com/erincatto/box3d) — Erin Catto's new open-source 3D physics engine — benchmarked side by side against Unity's built-in PhysX.

The sync layer follows the batched transform write-back design of Unity's official Physics Core 2D (the Box2D v3 binding): moved bodies are fetched from native code in a single call into a contiguous buffer, then written back to Transforms in parallel with a Burst-compiled `IJobParallelForTransform`. No per-body P/Invoke.

## Results

Measured on an Apple Silicon Mac (4P+6E cores), falling-tower scenario, matched materials and depenetration speed on both sides (per-step ms, PhysX / Box3D step+sync). Each engine runs in its own solo windows — stepping both in the same FixedUpdate inflates the numbers 10–20% through mutual cache/thermal interference (measured with an A-B-A test at 4096 bodies; the effect persists with zero Box3D threads, so it is not thread contention). Each body count runs two 500-step windows per engine in A-B-B-A order, cancelling the ~6–8% warm-up drift that otherwise penalizes whoever measures later:

| bodies | sleep enabled (game-like) | sleep disabled (full-load solver) |
|-------:|--------------------------:|----------------------------------:|
|    512 | 0.507 / **0.345** (1.47x) |  0.621 / **0.636** (0.98x) |
|   1024 | 0.966 / **0.573** (1.68x) |  1.046 / **0.912** (1.15x) |
|   2048 | 1.986 / **0.760** (2.61x) |  2.023 / **1.559** (1.30x) |
|   4096 | 4.274 / **1.888** (2.26x) |  4.332 / **3.126** (1.39x) |

- **Sleep enabled:** Box3D is 1.5–2.6x faster. Most of the gap comes from convergence quality — Box3D (TGS soft) puts a settled pile to sleep within seconds, while PhysX (PGS) keeps ~98% of bodies awake from contact jitter.
- **Sleep disabled:** parity at 512 bodies, Box3D pulling ahead as the count grows (1.0–1.4x).

Box3D's multithreading matters: single-threaded it loses at 4096 bodies (14.3 ms vs 6.6 ms). This binding plugs Box3D's task callbacks into Unity's C# Job System (`Box3DJobBridge`), so the solver runs wide on Unity's existing job workers and Box3D spawns no threads of its own — total thread count stays at the worker pool (~core count), and physics tasks show up in the profiler alongside every other job. The solve fans out with an adaptive worker count of `clamp(bodies / 128, 1, cores)`; correctness never depends on the pool, since the step thread runs worker 0 inline and can complete the whole step alone even if Unity's workers are busy.

Burst-compiling the write-back job is worth 17–29% of the whole step+sync (same-session A/B at 512–4096 bodies, sleep enabled). The per-body skip check reads each Transform's position and rotation, and that math is what Burst vectorizes away.

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

- **`Assets/Scenes/Benchmark.unity`** — PhysX (left) and Box3D (right) take turns in solo 500-step windows (A-B-B-A per body count), per-step ms in the HUD. `R` respawns, `↑`/`↓` doubles/halves the body count. An IMGUI toggle disables sleeping for pure solver comparison.
- **`Assets/Scenes/ComponentComparison.unity`** — the same tower and falling spheres on both engines for visual behavior comparison.

Both scenes can be regenerated via *Tools > Box3D > Build Comparison Scenes*.

## Components

- **`Box3DSimulation`** — one per scene; owns the world, steps it, and writes transforms back. Exposes `workerCount`.
- **`Box3DBody`** — Box or Sphere shape, Static or Dynamic.

## License

This project is released into the public domain under the [Unlicense](UNLICENSE). Box3D itself (referenced as a git submodule, not part of this repository) is MIT-licensed, Copyright (c) 2026 Erin Catto.
