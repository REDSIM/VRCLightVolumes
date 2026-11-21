#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRCLightVolumes {
    // GPU atlas packing: the same search space as the CPU packer, sized for the GPU. AtlasPacking.compute packs one candidate per
    // thread group with the CPU packer's placement rules, so a whole population is evaluated per dispatch. The host breeds the
    // candidates and keeps the best; the winner is packed again by the CPU packer, which also checks the layout.
    public static partial class Texture3DAtlasGenerator {

        private const string gpuPackingShaderPath = "Packages/red.sim.lightvolumes/Scripts/Editor/AtlasPacking.compute";
        private const string gpuPackingKernelName = "PackAtlasCandidates";
        // The kernel holds at most this many blocks (MAX_BLOCKS - 1 in the compute shader).
        private const int gpuPackingMaxBlocks = 255;
        // Below this many blocks the CPU search is as fast as the GPU one.
        private const int gpuPackingMinBlocks = 64;
        private const int gpuPackingPopulation = 1024;
        private const int gpuPackingElites = 102;
        private const int gpuPackingMutants = 102;
        private const int gpuPackingNeighbours = 410;
        private const int gpuPackingNeighbourParents = 25;
        private const int gpuPackingSeeds = 64;
        private const int gpuPackingChains = 32;
        private const int gpuPackingFineScanTops = 4;
        private const int gpuPackingFineScanRadius = 12;
        // Dispatches start small as a probe, then are sized from the measured time to about gpuPackingTargetDispatchMilliseconds,
        // far below the GPU watchdog timeout (about 2 s) even on slow integrated GPUs.
        private const int gpuPackingFirstGroupsPerDispatch = 256;
        private const int gpuPackingMinGroupsPerDispatch = 64;
        private const int gpuPackingMaxGroupsPerDispatch = 4096;
        private const double gpuPackingTargetDispatchMilliseconds = 100;
        // A readback that takes longer than this (plus four times the expected time) counts as a GPU failure.
        private const double gpuPackingReadbackTimeoutSeconds = 10;
        private const int gpuPackingGroupSize = 64;
        private const int gpuPackingBufferCount = 4;
        private const uint gpuPackingNoBound = 0xffffffffu;
        private const double gpuPackingStoppedRank = 1e15;
        // Time per editor update the search may wait for the GPU before it yields.
        private const int gpuPackingMillisecondsPerUpdate = 25;
        // Dispatched search steps per search level (Fast, Default, Thorough): genetic generations, then polish steps. Default and
        // Thorough share the genetic stage, so Thorough continues the Default search and never ends with a larger atlas.
        private static readonly int[] gpuGeneticSteps = { 20, 40, 40 };
        private static readonly int[] gpuPolishSteps = { 60, 300, 1200 };
        // Reasons the GPU could not pack that were already logged, so each is logged once per editor session.
        private static readonly HashSet<string> gpuPackingLoggedReasons = new HashSet<string>();
        private const string gpuDisabledReason = "The GPU is turned off for packing because Unity closed while it was packing on the GPU (a crash or a graphics " +
            "driver reset). Click Try GPU again on the Light Volume Manager to use it again.";
        private const string packingResultCpuFallback = "The atlas is packed on the CPU instead. It will be correct; packing only takes longer.";
        private const string packingActionUseCpu = "Nothing is needed. If this happens again, turn on Light Volume Manager > Packing Settings > Use CPU, " +
            "and report this log on the Light Volumes GitHub page.";
        private static readonly int editorProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
        private static string gpuPackingRunningKey;
        private static string gpuPackingDisabledKey;

        private enum GpuPackingFault { None, NoComputeSupport, MissingKernel, ReadbackError, InvalidResults }
        // Test hook: simulates a GPU fault so tests can check each CPU fallback. Always None outside tests.
        private static GpuPackingFault gpuPackingSimulatedFault;

        // A GPU packing failure with a few words for the packing log line.
        private sealed class GpuPackingException : Exception {
            public readonly string ShortReason;

            public GpuPackingException(string shortReason, string message) : base(message) {
                ShortReason = shortReason;
            }
        }

        // True when a crash or a graphics driver reset during GPU packing turned the GPU off for packing in this project on this
        // computer. Packing then runs on the CPU until EnableGpuPackingAgain is called (the Try GPU again button).
        public static bool GpuPackingDisabledAfterCrash => IsGpuPackingDisabledAfterCrash();

        // Lets packing use the GPU again after GpuPackingDisabledAfterCrash.
        public static void EnableGpuPackingAgain() {
            InitGpuPackingKeys();
            EditorPrefs.DeleteKey(gpuPackingDisabledKey);
            EditorPrefs.DeleteKey(gpuPackingRunningKey);
        }

        // Returns why these blocks pack on the CPU (a sentence, and a few words for the packing log line), or null with the loaded shader
        // and kernel when the GPU packs them. Device problems are logged once per reason.
        private static string GetGpuPackingSkipReason(AtlasBlock[] blocks, int padding, TexturePackingStrategy packingStrategy, AtlasPackingSettings packingSettings,
            out PackingProblem problem, out ComputeShader shader, out int kernel, out string shortReason) {
            problem = null;
            shader = null;
            kernel = -1;
            shortReason = null;
            if (packingSettings.UseCpu) {
                shortReason = "Use CPU is on";
                return "Use CPU is on.";
            }
            if (!string.IsNullOrEmpty(packingSettings.SkipGpuReason)) {
                shortReason = packingSettings.SkipGpuReason;
                return $"{packingSettings.SkipGpuReason}.";
            }
            if (IsGpuPackingDisabledAfterCrash()) {
                shortReason = "GPU packing is off after a crash; click Try GPU again on the Light Volume Manager to turn it back on";
                return gpuDisabledReason;
            }
            if (!TryCreatePackingProblem(blocks, padding, out problem)) {
                shortReason = "a volume texture is too large";
                return DescribeOversizedBlock(blocks, padding);
            }
            if (packingStrategy != TexturePackingStrategy.MinimumVRAM) {
                shortReason = "atlas post-processors are set";
                return "Atlas post-processors are set. Their packing mode (smallest depth first) always runs on the CPU.";
            }
            if (problem.Count < gpuPackingMinBlocks) {
                shortReason = $"{problem.Count} {(problem.Count == 1 ? "texture" : "textures")}; the GPU is used from {gpuPackingMinBlocks}";
                return $"The scene has {problem.Count} unique volume textures. Below {gpuPackingMinBlocks} the CPU is just as fast.";
            }
            if (problem.Count > gpuPackingMaxBlocks) {
                shortReason = $"{problem.Count} textures; the GPU packer holds up to {gpuPackingMaxBlocks}";
                return $"The scene has {problem.Count} unique volume textures. The GPU packer holds at most {gpuPackingMaxBlocks}.";
            }
            if (problem.PaddedVolume >= gpuPackingNoBound) {
                shortReason = "too many voxels for the GPU";
                return $"The volume textures have {problem.PaddedVolume:N0} voxels with padding, more than the GPU packer counts ({gpuPackingNoBound - 1:N0}).";
            }
            string reason = GetGpuPackingUnsupportedReason(out shader, out kernel, out shortReason, out bool unexpected);
            if (reason != null && gpuPackingLoggedReasons.Add(reason)) {
                string message = FormatPackingLog($"the GPU cannot run the packer ({shortReason}), so the atlas is packed on the CPU.", reason, packingResultCpuFallback,
                    unexpected ? "Nothing is needed. To restore GPU packing, reinstall or update the Light Volumes package; if that does not help, report this log on the Light Volumes GitHub page."
                        : "Nothing is needed. This message is shown once per Unity session.",
                    $"stage: checking the GPU before packing; {DescribeBlocks(blocks, padding)}");
                if (unexpected) Debug.LogWarning(message);
                else Debug.Log(message);
            }
            return reason;
        }

        // Returns why the editor's graphics device cannot run the packing kernel, or null with the loaded shader and kernel. unexpected
        // marks problems a working package should not have (a missing or broken shader) rather than hardware without compute support.
        private static string GetGpuPackingUnsupportedReason(out ComputeShader shader, out int kernel, out string shortReason, out bool unexpected) {
            shader = null;
            kernel = -1;
            unexpected = false;
            GraphicsDeviceType deviceType = SystemInfo.graphicsDeviceType;
            string device = $"{SystemInfo.graphicsDeviceName} ({deviceType})";
            if (deviceType == GraphicsDeviceType.Null) {
                shortReason = "no graphics device";
                return "Unity runs without a graphics device (for example in batch mode with -nographics).";
            }
            if (deviceType == GraphicsDeviceType.OpenGLES2 || deviceType == GraphicsDeviceType.OpenGLES3) {
                shortReason = $"{deviceType} not supported";
                return $"The editor runs on {deviceType}. The GPU packer needs Direct3D 11 or 12, Vulkan, Metal or OpenGL Core.";
            }
            if (!SystemInfo.supportsComputeShaders || gpuPackingSimulatedFault == GpuPackingFault.NoComputeSupport) {
                shortReason = "no compute shaders";
                return $"{device} does not support compute shaders.";
            }
            if (SystemInfo.maxComputeWorkGroupSize < gpuPackingGroupSize || SystemInfo.maxComputeWorkGroupSizeX < gpuPackingGroupSize) {
                shortReason = "thread group limit";
                return $"{device} runs at most {Math.Min(SystemInfo.maxComputeWorkGroupSize, SystemInfo.maxComputeWorkGroupSizeX)} compute threads per group; the packer needs {gpuPackingGroupSize}.";
            }
            if (SystemInfo.maxComputeBufferInputsCompute < gpuPackingBufferCount) {
                shortReason = "compute buffer limit";
                return $"{device} binds at most {SystemInfo.maxComputeBufferInputsCompute} compute buffers; the packer needs {gpuPackingBufferCount}.";
            }
            unexpected = true;
            shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(gpuPackingShaderPath);
            if (shader == null) {
                shortReason = "packing shader missing";
                return $"The packing shader {gpuPackingShaderPath} is missing or failed to import.";
            }
            if (!shader.HasKernel(gpuPackingKernelName) || gpuPackingSimulatedFault == GpuPackingFault.MissingKernel) {
                shortReason = "packing shader did not compile";
                return $"The packing shader has no {gpuPackingKernelName} kernel for {deviceType}, so it did not compile there. {GetComputeShaderErrors(shader)}";
            }
            kernel = shader.FindKernel(gpuPackingKernelName);
            if (!shader.IsSupported(kernel)) {
                shortReason = "packing shader not supported";
                return $"The {gpuPackingKernelName} kernel is not supported on {device} (it needs about 26 KB of groupshared memory and {gpuPackingGroupSize} threads per group). {GetComputeShaderErrors(shader)}";
            }
            unexpected = false;
            shortReason = null;
            return null;
        }

        // Lists the compile errors Unity reports for the packing shader.
        private static string GetComputeShaderErrors(ComputeShader shader) {
            List<string> errors = new List<string>();
            foreach (ShaderMessage message in ShaderUtil.GetComputeShaderMessages(shader)) {
                if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) errors.Add($"{message.platform}: {message.message} ({message.file}:{message.line})");
            }
            return errors.Count == 0 ? "Unity reports no compile errors for it." : $"Unity reports these compile errors: {string.Join("; ", errors)}";
        }

        // Crash guard. While the GPU packs, the editor's process ID is saved in EditorPrefs for this project. If an editor finds the ID of
        // another process there, that editor closed during GPU packing (a crash, or a graphics driver reset that took it down), so the
        // GPU is turned off for packing until the user turns it back on. A script reload or a normal quit clears the ID.
        [InitializeOnLoadMethod]
        private static void CheckGpuPackingCrashOnLoad() {
            IsGpuPackingDisabledAfterCrash();
        }

        private static bool IsGpuPackingDisabledAfterCrash() {
            InitGpuPackingKeys();
            int runningProcess = EditorPrefs.GetInt(gpuPackingRunningKey, 0);
            if (runningProcess != 0 && runningProcess != editorProcessId) {
                EditorPrefs.DeleteKey(gpuPackingRunningKey);
                EditorPrefs.SetBool(gpuPackingDisabledKey, true);
                Debug.LogWarning(FormatPackingLog("Unity closed while the atlas was being packed on the GPU, so GPU packing is turned off.",
                    $"The previous Unity session (process {runningProcess}) ended during GPU packing: a crash, a graphics driver reset, or Unity being closed by force.",
                    "From now on the atlas is packed on the CPU on this computer. It is still correct; packing only takes longer.",
                    "To use the GPU again, click Try GPU again on the Light Volume Manager. If Unity closes during packing again, turn on Packing Settings > " +
                    "Use CPU and report this log on the Light Volumes GitHub page.",
                    "stage: Unity start"));
            }
            return EditorPrefs.GetBool(gpuPackingDisabledKey, false);
        }

        private static void MarkGpuPackingRunning() {
            InitGpuPackingKeys();
            EditorPrefs.SetInt(gpuPackingRunningKey, editorProcessId);
            AssemblyReloadEvents.beforeAssemblyReload += ClearGpuPackingRunning;
            EditorApplication.quitting += ClearGpuPackingRunning;
        }

        private static void ClearGpuPackingRunning() {
            AssemblyReloadEvents.beforeAssemblyReload -= ClearGpuPackingRunning;
            EditorApplication.quitting -= ClearGpuPackingRunning;
            InitGpuPackingKeys();
            EditorPrefs.DeleteKey(gpuPackingRunningKey);
        }

        // EditorPrefs are shared by all projects on the computer, so the keys include a hash of this project's path.
        private static void InitGpuPackingKeys() {
            if (gpuPackingRunningKey != null) return;
            string project = Hash128.Compute(Application.dataPath).ToString();
            gpuPackingRunningKey = $"VRCLightVolumes.AtlasPacking.GpuRunning.{project}";
            gpuPackingDisabledKey = $"VRCLightVolumes.AtlasPacking.GpuDisabled.{project}";
        }

        // Runs the GPU search while the crash guard marks the GPU as running. On success output holds the checked result; on failure it
        // gets the reason (a sentence and a few words for the packing log line), a warning is logged and the caller packs on the CPU.
        private static IEnumerator RunGpuPacking(PackingProblem problem, ComputeShader shader, int kernel, AtlasBlock[] blocks, int padding, TexturePackingStrategy packingStrategy,
            AtlasPackingSettings packingSettings, Action<float, string> reportProgress, CancellationToken cancellationToken, PackingOutput output, GpuPackingOutcome outcome) {
            ThreadProgress gpuProgress = new ThreadProgress { Total = 1 };
            IEnumerator gpuSearch = SearchPackingOnGpu(problem, shader, kernel, packingSettings, gpuProgress, cancellationToken, outcome);
            string failure = null, failureShort = null;
            MarkGpuPackingRunning();
            try {
                while (true) {
                    reportProgress?.Invoke(GetProgress(gpuProgress), $"Packing {blocks.Length} volume textures on the GPU");
                    bool running;
                    try {
                        running = gpuSearch.MoveNext();
                    } catch (Exception exception) {
                        GpuPackingException gpuException = exception as GpuPackingException;
                        failureShort = gpuException != null ? gpuException.ShortReason : "unexpected error";
                        // Errors the packer does not raise itself come with their type and stack trace for a bug report.
                        failure = gpuException != null ? gpuException.Message : $"Unexpected error: {exception}";
                        break;
                    }
                    if (!running) break;
                    yield return gpuSearch.Current;
                }
            } finally {
                (gpuSearch as IDisposable)?.Dispose();
                ClearGpuPackingRunning();
            }
            if (cancellationToken.IsCancellationRequested) yield break;
            if (failure == null && outcome.Best.Result.Valid) {
                PackingResult result = CreatePackingResult(problem, outcome.Best, packingStrategy);
                if (result.Success) {
                    result.Packings = outcome.Packings;
                    result.TimeLimitReached = outcome.TimeLimitReached;
                    long packedVolume = (long)result.AtlasWidth * result.AtlasHeight * result.AtlasDepth;
                    // The kernel drops empty spaces when its space slots overflow, so the CPU repack can be larger than the GPU reported. Still
                    // valid; logged once per session as a hint for bug reports.
                    if (packedVolume > outcome.Best.Result.Volume && gpuPackingLoggedReasons.Add("repack larger")) {
                        Debug.Log(FormatPackingLog($"the CPU check of the GPU's best layout came out {100.0 * packedVolume / outcome.Best.Result.Volume - 100.0:0.0} % larger than the GPU reported.",
                            $"The GPU reported {outcome.Best.Result.Width} x {outcome.Best.Result.Height} x {outcome.Best.Result.Depth}; the CPU packed the same order and caps into {result.AtlasWidth} x {result.AtlasHeight} x {result.AtlasDepth}. This happens when a candidate has more empty spaces than the kernel holds.",
                            "The CPU layout passed the check and is used. The atlas is correct.", "Nothing is needed. This message is shown once per Unity session.",
                            $"stage: CPU check of the GPU result; {DescribeBlocks(blocks, padding)}"));
                    }
                    output.Result = result;
                    output.UsedGpu = true;
                    yield break;
                }
                failureShort = "layout check failed";
                failure = $"The CPU check of the GPU's best layout failed. {result.Error}";
            } else if (failure == null) {
                failureShort = "no layout found";
                failure = $"The GPU search found no layout that fits within {maxAtlasSize} voxels per axis.";
            }
            output.GpuFailed = true;
            output.CpuReason = failure;
            output.CpuReasonShort = failureShort;
            PackingCandidate best = outcome.Search != null ? outcome.Search.Best : default;
            string bestAtlas = best.Result.Valid ? $"best atlas so far {best.Result.Width} x {best.Result.Height} x {best.Result.Depth}" : "no atlas found yet";
            Debug.LogWarning(FormatPackingLog($"packing on the GPU failed ({failureShort}), so the atlas is packed on the CPU instead.", failure, packingResultCpuFallback,
                packingActionUseCpu, $"stage: {outcome.Stage}; {DescribeBlocks(blocks, padding)}; {bestAtlas}; {outcome.Evaluator?.Packings ?? 0} candidates evaluated; search {packingSettings.Search}"));
        }

        // Searches on the GPU and stores the best candidate in outcome. Yields while each step runs, so the editor stays responsive.
        // The steps mirror the CPU search: the cap grid without a bound so true sizes rank the seeds, every cap pair close to the best
        // few, a memetic biased random-key genetic algorithm (elites, children, mutants and local-search neighbours of the elites),
        // then parallel local-search chains that swap or move blocks in the order and nudge the caps. Throws on GPU errors.
        private static IEnumerator SearchPackingOnGpu(PackingProblem problem, ComputeShader shader, int kernel, AtlasPackingSettings packingSettings, ThreadProgress progress, CancellationToken cancellationToken, GpuPackingOutcome outcome) {

            PackingClock clock = new PackingClock(packingSettings.MaxSeconds);
            int level = GetPackingSearchLevel(packingSettings.Search);
            int generations = gpuGeneticSteps[level];
            int polishSteps = gpuPolishSteps[level];
            int generationPlateau = packingSettings.StopEarly ? Math.Max(1, generations / packingPlateauDivisor) : int.MaxValue;
            int polishPlateau = packingSettings.StopEarly ? Math.Max(1, polishSteps / packingPlateauDivisor) : int.MaxValue;
            int count = problem.Count;
            int genes = count + 3;
            int fineSide = 2 * gpuPackingFineScanRadius + 1;

            List<PackingRun> runs = new List<PackingRun>();
            for (int k = 0; k < packingOrderCount; k++) runs.Add(new PackingRun { FreeAxis = packingUnboundedAxis, Order = k, CapX = maxAtlasSize, CapY = maxAtlasSize });
            runs.AddRange(CreateCappedPackingRuns(problem));
            int capacity = Math.Max(gpuPackingPopulation, Math.Max(runs.Count, gpuPackingFineScanTops * fineSide * fineSide * packingOrderCount));
            GpuPackingSearch search = new GpuPackingSearch(problem);
            outcome.Search = search;
            outcome.Stage = "allocating GPU buffers";
            GpuPackingEvaluator evaluator = new GpuPackingEvaluator(shader, kernel, problem, capacity);
            outcome.Evaluator = evaluator;
            try {
                progress.Total = 2 + generations + polishSteps;

                // 1. The cap grid without a bound.
                outcome.Stage = "cap grid";
                for (int r = 0; r < runs.Count; r++) evaluator.Set(r, runs[r].FreeAxis, runs[r].CapX, runs[r].CapY, gpuPackingNoBound, problem.Orders[runs[r].Order]);
                evaluator.Submit(runs.Count);
                while (!evaluator.Poll()) {
                    if (cancellationToken.IsCancellationRequested) yield break;
                    yield return null;
                }
                search.Track(evaluator, runs.Count);
                List<double> ranks = new List<double>(runs.Count);
                for (int r = 0; r < runs.Count; r++) ranks.Add(evaluator.GetRank(r));
                Interlocked.Increment(ref progress.Processed);

                // 2. Every cap pair close to the best distinct capped runs, bounded by the best atlas.
                HashSet<long> seen = new HashSet<long>();
                for (int r = 0; r < runs.Count; r++) seen.Add(GetPackingCapsKey(runs[r].FreeAxis, runs[r].CapX, runs[r].CapY));
                List<PackingRun> fine = new List<PackingRun>();
                foreach (int top in SelectTopGpuRuns(runs, ranks, gpuPackingFineScanTops)) {
                    PackingRun winner = runs[top];
                    int axisX, axisY, axisZ;
                    GetPackingFrameAxes(winner.FreeAxis, out axisX, out axisY, out axisZ);
                    for (int capX = Math.Max(problem.MaxSize[axisX], winner.CapX - gpuPackingFineScanRadius); capX <= Math.Min(maxAtlasSize, winner.CapX + gpuPackingFineScanRadius); capX++) {
                        for (int capY = Math.Max(problem.MaxSize[axisY], winner.CapY - gpuPackingFineScanRadius); capY <= Math.Min(maxAtlasSize, winner.CapY + gpuPackingFineScanRadius); capY++) {
                            if (!seen.Add(GetPackingCapsKey(winner.FreeAxis, capX, capY))) continue;
                            for (int k = 0; k < packingOrderCount; k++) fine.Add(new PackingRun { FreeAxis = winner.FreeAxis, Order = k, CapX = capX, CapY = capY });
                        }
                    }
                }
                if (fine.Count > 0 && !clock.Expired) {
                    outcome.Stage = "fine cap scan";
                    for (int r = 0; r < fine.Count; r++) evaluator.Set(r, fine[r].FreeAxis, fine[r].CapX, fine[r].CapY, search.GetBound(1f), problem.Orders[fine[r].Order]);
                    evaluator.Submit(fine.Count);
                    while (!evaluator.Poll()) {
                        if (cancellationToken.IsCancellationRequested) yield break;
                        yield return null;
                    }
                    search.Track(evaluator, fine.Count);
                    runs.AddRange(fine);
                    for (int r = 0; r < fine.Count; r++) ranks.Add(evaluator.GetRank(r));
                }
                Interlocked.Increment(ref progress.Processed);

                // 3. Genetic search seeded with the best distinct capped runs. Runs may end up to 1/20 above the best atlas, so
                //    near misses still rank by size.
                float[][] population = new float[gpuPackingPopulation][];
                double[] fitness = new double[gpuPackingPopulation];
                List<int> seeds = SelectTopGpuRuns(runs, ranks, gpuPackingSeeds);
                for (int p = 0; p < gpuPackingPopulation; p++) {
                    population[p] = new float[genes];
                    for (int g = 0; g < genes; g++) population[p][g] = search.NextKey();
                    if (p < seeds.Count) EncodePackingRun(population[p], runs[seeds[p]], problem.Orders[runs[seeds[p]].Order], problem.MaxSize, count);
                }
                int first = 0;
                int sinceImprovement = 0;
                int[] rank = new int[gpuPackingPopulation];
                for (int generation = 0; generation <= generations; generation++) {
                    if (generation > 0) {
                        if (sinceImprovement >= generationPlateau || clock.Expired) break;
                        SortGpuPopulation(fitness, rank);
                        float[][] next = new float[gpuPackingPopulation][];
                        double[] nextFitness = new double[gpuPackingPopulation];
                        for (int p = 0; p < gpuPackingElites; p++) {
                            next[p] = population[rank[p]];
                            nextFitness[p] = fitness[rank[p]];
                        }
                        int children = gpuPackingPopulation - gpuPackingElites - gpuPackingMutants - gpuPackingNeighbours;
                        for (int p = gpuPackingElites; p < gpuPackingElites + children; p++) {
                            float[] elite = population[rank[search.NextInt(gpuPackingElites)]];
                            float[] other = population[rank[gpuPackingElites + search.NextInt(gpuPackingPopulation - gpuPackingElites)]];
                            float[] child = new float[genes];
                            for (int g = 0; g < genes; g++) child[g] = search.NextKey() < packingEliteInheritance ? elite[g] : other[g];
                            next[p] = child;
                        }
                        for (int p = gpuPackingElites + children; p < gpuPackingPopulation - gpuPackingNeighbours; p++) {
                            float[] mutant = new float[genes];
                            for (int g = 0; g < genes; g++) mutant[g] = search.NextKey();
                            next[p] = mutant;
                        }
                        for (int p = gpuPackingPopulation - gpuPackingNeighbours; p < gpuPackingPopulation; p++) {
                            next[p] = search.CreateNeighbour(population[rank[search.NextInt(gpuPackingNeighbourParents)]]);
                        }
                        population = next;
                        fitness = nextFitness;
                        first = gpuPackingElites;
                    }
                    outcome.Stage = $"genetic search, generation {generation}";
                    uint bound = search.GetBound(1f + 1f / packingSlackDivisor);
                    float[][] decodePopulation = population;
                    int decodeFirst = first;
                    Parallel.For(first, gpuPackingPopulation, () => new GpuDecodeBuffers(count), (p, loopState, buffers) => {
                        DecodeGpuKeys(decodePopulation[p], problem, evaluator, p - decodeFirst, bound, buffers);
                        return buffers;
                    }, buffers => { });
                    evaluator.Submit(gpuPackingPopulation - first);
                    while (!evaluator.Poll()) {
                        if (cancellationToken.IsCancellationRequested) yield break;
                        yield return null;
                    }
                    sinceImprovement = search.Track(evaluator, gpuPackingPopulation - first) ? 0 : sinceImprovement + 1;
                    for (int p = first; p < gpuPackingPopulation; p++) fitness[p] = evaluator.GetRank(p - first);
                    if (generation > 0) Interlocked.Increment(ref progress.Processed);
                }
                Volatile.Write(ref progress.Processed, 2 + generations);

                // 4. Local search chains from the best distinct chromosomes, caps and free axis per chain. Each step tries
                //    gpuPackingPopulation / gpuPackingChains single moves per chain in one dispatch and moves every chain to its best
                //    move that does not grow its atlas.
                outcome.Stage = "local search start";
                SortGpuPopulation(fitness, rank);
                int chains = Math.Min(gpuPackingChains, gpuPackingPopulation);
                int moves = gpuPackingPopulation / chains;
                GpuDecodeBuffers decodeBuffers = new GpuDecodeBuffers(count);
                PackingRun[] chainRuns = new PackingRun[chains];
                int[][] chainOrders = new int[chains][];
                uint[] chainVolumes = new uint[chains];
                for (int c = 0; c < chains; c++) {
                    DecodeGpuKeys(population[rank[c]], problem, evaluator, c, gpuPackingNoBound, decodeBuffers);
                    chainRuns[c] = evaluator.GetRun(c);
                    chainOrders[c] = evaluator.GetOrder(c);
                }
                evaluator.Submit(chains);
                while (!evaluator.Poll()) {
                    if (cancellationToken.IsCancellationRequested) yield break;
                    yield return null;
                }
                search.Track(evaluator, chains);
                for (int c = 0; c < chains; c++) chainVolumes[c] = evaluator.GetVolume(c);

                sinceImprovement = 0;
                int[] trialOrder = new int[count];
                for (int step = 0; step < polishSteps && sinceImprovement < polishPlateau && !clock.Expired; step++) {
                    outcome.Stage = $"local search, step {step}";
                    for (int c = 0; c < chains; c++) {
                        for (int m = 0; m < moves; m++) {
                            PackingRun trialRun = chainRuns[c];
                            search.CreateMove(chainOrders[c], trialOrder, ref trialRun);
                            evaluator.Set(c * moves + m, trialRun.FreeAxis, trialRun.CapX, trialRun.CapY, chainVolumes[c], trialOrder);
                        }
                    }
                    evaluator.Submit(chains * moves);
                    while (!evaluator.Poll()) {
                        if (cancellationToken.IsCancellationRequested) yield break;
                        yield return null;
                    }
                    sinceImprovement = search.Track(evaluator, chains * moves) ? 0 : sinceImprovement + 1;
                    for (int c = 0; c < chains; c++) {
                        int best = -1;
                        for (int m = 0; m < moves; m++) {
                            int slot = c * moves + m;
                            if (evaluator.GetVolume(slot) <= chainVolumes[c] && (best < 0 || evaluator.GetVolume(slot) < evaluator.GetVolume(best))) best = slot;
                        }
                        if (best < 0) continue;
                        chainVolumes[c] = evaluator.GetVolume(best);
                        chainRuns[c] = evaluator.GetRun(best);
                        chainOrders[c] = evaluator.GetOrder(best);
                    }
                    Interlocked.Increment(ref progress.Processed);
                }
                outcome.Best = search.Best;
                outcome.Packings = evaluator.Packings;
                outcome.TimeLimitReached = clock.LimitReached;
            } finally {
                evaluator.Dispose();
            }
        }

        // Returns the best distinct capped runs by rank, skipping runs that did not finish.
        private static List<int> SelectTopGpuRuns(List<PackingRun> runs, List<double> ranks, int limit) {
            int[] order = new int[runs.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => {
                int c = ranks[a].CompareTo(ranks[b]);
                return c != 0 ? c : a.CompareTo(b);
            });
            List<int> top = new List<int>(limit);
            HashSet<long> taken = new HashSet<long>();
            for (int i = 0; i < order.Length && top.Count < limit; i++) {
                PackingRun run = runs[order[i]];
                if (run.FreeAxis == packingUnboundedAxis || ranks[order[i]] >= gpuPackingStoppedRank) continue;
                if (taken.Add(GetPackingCapsKey(run.FreeAxis, run.CapX, run.CapY))) top.Add(order[i]);
            }
            return top;
        }

        // Ranks a population by fitness, the lowest index on ties.
        private static void SortGpuPopulation(double[] fitness, int[] rank) {
            for (int p = 0; p < rank.Length; p++) rank[p] = p;
            Array.Sort(rank, (a, b) => {
                int c = fitness[a].CompareTo(fitness[b]);
                return c != 0 ? c : a.CompareTo(b);
            });
        }

        // Decodes random keys like DecodePackingKeys into an evaluator slot.
        private static void DecodeGpuKeys(float[] keys, PackingProblem problem, GpuPackingEvaluator evaluator, int slot, uint bound, GpuDecodeBuffers buffers) {
            int count = problem.Count;
            int freeAxis = Math.Min(2, (int)(keys[count + 2] * 3f));
            int axisX, axisY, axisZ;
            GetPackingFrameAxes(freeAxis, out axisX, out axisY, out axisZ);
            int capX = Math.Min(maxAtlasSize, problem.MaxSize[axisX] + (int)(keys[count] * 2f * problem.MaxSize[axisX]));
            int capY = Math.Min(maxAtlasSize, problem.MaxSize[axisY] + (int)(keys[count + 1] * 2f * problem.MaxSize[axisY]));
            Array.Copy(keys, buffers.Keys, count);
            for (int i = 0; i < count; i++) buffers.Order[i] = i;
            Array.Sort(buffers.Keys, buffers.Order);
            evaluator.Set(slot, freeAxis, capX, capY, bound, buffers.Order);
        }

        private sealed class GpuDecodeBuffers {
            public readonly float[] Keys;
            public readonly int[] Order;

            public GpuDecodeBuffers(int count) {
                Keys = new float[count];
                Order = new int[count];
            }
        }

        // Result of a GPU search, and where it got to for failure logs.
        private sealed class GpuPackingOutcome {
            public PackingCandidate Best;
            public int Packings;
            public bool TimeLimitReached;
            public string Stage = "starting";
            public GpuPackingSearch Search;
            public GpuPackingEvaluator Evaluator;
        }

        // Host state of one GPU search: the best candidate so far and the random moves.
        private sealed class GpuPackingSearch {
            private readonly PackingProblem _problem;
            private uint _state = packingRandomSeed;
            public PackingCandidate Best;

            public GpuPackingSearch(PackingProblem problem) {
                _problem = problem;
            }

            // Keeps the smallest finished atlas of the last evaluation; returns whether it beat the best so far.
            public bool Track(GpuPackingEvaluator evaluator, int candidateCount) {
                int best = -1;
                for (int i = 0; i < candidateCount; i++) {
                    if (evaluator.Results[i].Status == 0 && (best < 0 || evaluator.Results[i].Value < evaluator.Results[best].Value)) best = i;
                }
                if (best < 0 || (Best.Result.Valid && evaluator.Results[best].Value >= Best.Result.Volume)) return false;
                GpuPackingResult result = evaluator.Results[best];
                Best = new PackingCandidate {
                    Run = evaluator.GetRun(best),
                    Order = evaluator.GetOrder(best),
                    Result = new PackingRunResult { Valid = true, Width = (int)(result.Extent & 0xffffu), Height = (int)(result.Extent >> 16), Depth = (int)result.Depth, Volume = result.Value }
                };
                return true;
            }

            // Returns the best volume times the slack, or no bound before the first finished atlas.
            public uint GetBound(float slack) {
                if (!Best.Result.Valid) return gpuPackingNoBound;
                return (uint)Math.Min(gpuPackingNoBound - 1, (long)(Best.Result.Volume * (double)slack));
            }

            public float NextKey() {
                return NextPackingKey(ref _state);
            }

            public int NextInt(int range) {
                return (int)(NextPackingRandom(ref _state) % (uint)range);
            }

            // Copies keys with one to three local moves: swap adjacent or random blocks in the order, or nudge a cap key.
            public float[] CreateNeighbour(float[] parent) {
                int count = _problem.Count;
                float[] keys = (float[])parent.Clone();
                int moveCount = 1 + NextInt(3);
                int[] order = null;
                for (int move = 0; move < moveCount; move++) {
                    int kind = NextInt(6);
                    if (kind < 5) {
                        if (order == null) order = new int[count];
                        for (int i = 0; i < count; i++) order[i] = i;
                        float[] sorted = (float[])keys.Clone();
                        Array.Sort(sorted, order, 0, count);
                        int first = NextInt(count);
                        int second = kind < 3 ? Math.Min(count - 1, first + 1) : NextInt(count);
                        int a = order[first], b = order[second];
                        float swap = keys[a];
                        keys[a] = keys[b];
                        keys[b] = swap;
                    } else {
                        int gene = count + NextInt(2);
                        keys[gene] = Math.Max(0f, Math.Min(0.9999f, keys[gene] + NextGaussian() * 0.02f));
                    }
                }
                return keys;
            }

            // Writes one local move of order into trialOrder or run: nudge a cap by 1 to 3, swap adjacent or random blocks, or
            // move one block to another place in the order.
            public void CreateMove(int[] order, int[] trialOrder, ref PackingRun run) {
                int count = _problem.Count;
                Array.Copy(order, trialOrder, count);
                int move = NextInt(8);
                int first = NextInt(count);
                int second = move == 1 || move == 2 ? Math.Min(count - 1, first + 1) : NextInt(count);
                if (move == 0) {
                    int axisX, axisY, axisZ;
                    GetPackingFrameAxes(run.FreeAxis, out axisX, out axisY, out axisZ);
                    int delta = (1 + NextInt(3)) * (NextInt(2) == 0 ? -1 : 1);
                    if (run.FreeAxis == packingUnboundedAxis) return;
                    if ((first & 1) == 0) run.CapX = Math.Max(_problem.MaxSize[axisX], Math.Min(maxAtlasSize, run.CapX + delta));
                    else run.CapY = Math.Max(_problem.MaxSize[axisY], Math.Min(maxAtlasSize, run.CapY + delta));
                } else if (move <= 4) {
                    int swap = trialOrder[first];
                    trialOrder[first] = trialOrder[second];
                    trialOrder[second] = swap;
                } else {
                    int moved = trialOrder[first];
                    if (first < second) Array.Copy(order, first + 1, trialOrder, first, second - first);
                    else Array.Copy(order, second, trialOrder, second + 1, first - second);
                    trialOrder[second] = moved;
                }
            }

            // Standard normal sample (Box-Muller).
            private float NextGaussian() {
                double u = 1.0 - NextKey();
                double v = NextKey();
                return (float)(Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v));
            }
        }

        private struct GpuPackingCandidate {
            public uint FreeAxis;
            public uint CapX;
            public uint CapY;
            public uint Bound;
        }

        private struct GpuPackingResult {
            public uint Status;
            public uint Value;
            public uint Extent;
            public uint Depth;
        }

        // Uploads candidates, dispatches the kernel in chunks and reads the results back asynchronously.
        private sealed class GpuPackingEvaluator : IDisposable {
            public readonly GpuPackingResult[] Results;
            public int Packings;
            private readonly GpuPackingCandidate[] _candidates;
            private readonly int[] _orders;
            private readonly int _count;
            private readonly ComputeShader _shader;
            private readonly int _kernel;
            private readonly uint _paddedVolume;
            private ComputeBuffer _sizeBuffer;
            private ComputeBuffer _candidateBuffer;
            private ComputeBuffer _orderBuffer;
            private ComputeBuffer _resultBuffer;
            private AsyncGPUReadbackRequest _request;
            private int _pendingCount;
            private bool _pending;
            private readonly System.Diagnostics.Stopwatch _sinceResume = System.Diagnostics.Stopwatch.StartNew();
            private readonly System.Diagnostics.Stopwatch _sinceSubmit = new System.Diagnostics.Stopwatch();
            private bool _yielded;
            private bool _yieldedSinceSubmit;
            private int _groupsPerDispatch = gpuPackingFirstGroupsPerDispatch;
            private double _millisecondsPerGroup;
            private double _timeoutSeconds;

            public GpuPackingEvaluator(ComputeShader shader, int kernel, PackingProblem problem, int capacity) {
                _shader = shader;
                _kernel = kernel;
                _count = problem.Count;
                _paddedVolume = (uint)problem.PaddedVolume;
                long orderBytes = (long)capacity * _count * sizeof(uint);
                if (orderBytes > SystemInfo.maxGraphicsBufferSize) {
                    throw new GpuPackingException("buffer size limit", $"The packing needs a {orderBytes:N0}-byte GPU buffer ({capacity} candidates x {_count} blocks); this GPU allows at most {SystemInfo.maxGraphicsBufferSize:N0} bytes per buffer.");
                }
                _candidates = new GpuPackingCandidate[capacity];
                _orders = new int[capacity * _count];
                Results = new GpuPackingResult[capacity];
                long totalBytes = orderBytes + (long)capacity * 8 * sizeof(uint) + _count * 3 * sizeof(uint);
                try {
                    _sizeBuffer = new ComputeBuffer(_count * 3, sizeof(uint));
                    _sizeBuffer.SetData(problem.Size);
                    _candidateBuffer = new ComputeBuffer(capacity, 4 * sizeof(uint));
                    _orderBuffer = new ComputeBuffer(capacity * _count, sizeof(uint));
                    _resultBuffer = new ComputeBuffer(capacity, 4 * sizeof(uint));
                    if (!_sizeBuffer.IsValid() || !_candidateBuffer.IsValid() || !_orderBuffer.IsValid() || !_resultBuffer.IsValid()) {
                        throw new GpuPackingException("buffer allocation failed", $"Allocating {totalBytes:N0} bytes of GPU buffers failed (the buffers are not valid). The GPU may be out of video memory.");
                    }
                } catch (Exception exception) {
                    Dispose();
                    if (exception is GpuPackingException) throw;
                    throw new GpuPackingException("buffer allocation failed", $"Allocating {totalBytes:N0} bytes of GPU buffers failed: {exception.GetType().Name}: {exception.Message}");
                }
                // Releases the buffers if scripts reload while a search is suspended.
                AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            }

            public void Set(int slot, int freeAxis, int capX, int capY, uint bound, int[] order) {
                _candidates[slot] = new GpuPackingCandidate { FreeAxis = (uint)freeAxis, CapX = (uint)capX, CapY = (uint)capY, Bound = bound };
                Array.Copy(order, 0, _orders, slot * _count, _count);
            }

            public PackingRun GetRun(int slot) {
                GpuPackingCandidate candidate = _candidates[slot];
                return new PackingRun { FreeAxis = (int)candidate.FreeAxis, CapX = (int)candidate.CapX, CapY = (int)candidate.CapY };
            }

            public int[] GetOrder(int slot) {
                int[] order = new int[_count];
                Array.Copy(_orders, slot * _count, order, 0, _count);
                return order;
            }

            // Finished atlas volume, or the largest volume if the candidate did not finish.
            public uint GetVolume(int slot) {
                return Results[slot].Status == 0 ? Results[slot].Value : gpuPackingNoBound - 1;
            }

            // Fitness rank: finished atlases by volume, then candidates stopped by the bound by how far they got, then the ones that did not fit.
            public double GetRank(int slot) {
                GpuPackingResult result = Results[slot];
                if (result.Status == 0) return result.Value;
                return result.Status == 2 ? gpuPackingStoppedRank + result.Value : double.PositiveInfinity;
            }

            public void Submit(int candidateCount) {
                try {
                    _candidateBuffer.SetData(_candidates, 0, 0, candidateCount);
                    _orderBuffer.SetData(_orders, 0, 0, candidateCount * _count);
                    _shader.SetBuffer(_kernel, "_BlockSizes", _sizeBuffer);
                    _shader.SetBuffer(_kernel, "_Candidates", _candidateBuffer);
                    _shader.SetBuffer(_kernel, "_Orders", _orderBuffer);
                    _shader.SetBuffer(_kernel, "_Results", _resultBuffer);
                    _shader.SetInt("_BlockCount", _count);
                    _shader.SetInt("_PaddedVolume", unchecked((int)_paddedVolume));
                    _shader.SetInt("_CandidateCount", candidateCount);
                    for (int first = 0; first < candidateCount; first += _groupsPerDispatch) {
                        _shader.SetInt("_CandidateOffset", first);
                        _shader.Dispatch(_kernel, Math.Min(_groupsPerDispatch, candidateCount - first), 1, 1);
                    }
                } catch (Exception exception) {
                    throw new GpuPackingException("dispatch failed", $"Sending {candidateCount} candidates to the GPU ({_groupsPerDispatch} thread groups per dispatch) failed: {exception.GetType().Name}: {exception.Message}");
                }
                _pendingCount = candidateCount;
                Packings += candidateCount;
                _pending = true;
                _yieldedSinceSubmit = false;
                _timeoutSeconds = gpuPackingReadbackTimeoutSeconds + 4.0 * _millisecondsPerGroup * candidateCount / 1000.0;
                _sinceSubmit.Restart();
                if (SystemInfo.supportsAsyncGPUReadback) {
                    _request = AsyncGPUReadback.Request(_resultBuffer, candidateCount * 4 * sizeof(uint), 0);
                } else {
                    _resultBuffer.GetData(Results, 0, 0, candidateCount);
                    _pending = false;
                    if (gpuPackingSimulatedFault == GpuPackingFault.ReadbackError) throw ReadbackError();
                    CompleteResults();
                }
            }

            private GpuPackingException ReadbackError() {
                return new GpuPackingException("readback error", $"Reading the results of {_pendingCount} candidates back from the GPU failed (AsyncGPUReadback reported an error). " +
                    "This happens when the graphics driver was reset or the GPU was lost, for example after a driver timeout.");
            }

            // Returns true once the results of the last Submit are in Results; throws if the readback failed or timed out. While the
            // current editor update has time left it waits for the GPU, so several steps run per update; otherwise it returns false and
            // the caller yields. Some graphics APIs submit queued work only at the end of a frame or when waited on, so polling alone
            // would cost a frame per step. A GPU that hangs inside the wait is reset by the operating system's watchdog, which fails the
            // readback; the timeout catches readbacks that stay pending across updates.
            public bool Poll() {
                if (_yielded) {
                    _yielded = false;
                    _sinceResume.Restart();
                }
                if (!_pending) return true;
                if (!_request.done) _request.Update();
                if (!_request.done) {
                    if (_sinceSubmit.Elapsed.TotalSeconds > _timeoutSeconds) {
                        throw new GpuPackingException("timed out", $"The GPU did not return the results of {_pendingCount} candidates within {_timeoutSeconds:0.0} s " +
                            $"(expected about {_millisecondsPerGroup * _pendingCount / 1000.0:0.00} s). The GPU may be hung, or busy with other work such as a bake.");
                    }
                    if (_sinceResume.ElapsedMilliseconds >= gpuPackingMillisecondsPerUpdate) {
                        _yielded = true;
                        _yieldedSinceSubmit = true;
                        return false;
                    }
                    _request.WaitForCompletion();
                }
                _pending = false;
                if (_request.hasError || gpuPackingSimulatedFault == GpuPackingFault.ReadbackError) throw ReadbackError();
                NativeArray<GpuPackingResult>.Copy(_request.GetData<GpuPackingResult>(), 0, Results, 0, _pendingCount);
                CompleteResults();
                return true;
            }

            // Checks the results of the last Submit and sizes the next dispatches from the measured time, so each dispatch stays near
            // gpuPackingTargetDispatchMilliseconds on fast and slow GPUs alike. Throws on results no packing can produce.
            private void CompleteResults() {
                if (gpuPackingSimulatedFault == GpuPackingFault.InvalidResults) Results[0].Status = 7;
                for (int i = 0; i < _pendingCount; i++) {
                    GpuPackingResult result = Results[i];
                    bool valid = result.Status <= 2;
                    if (result.Status == 0) {
                        uint width = result.Extent & 0xffffu, height = result.Extent >> 16, depth = result.Depth;
                        ulong volume = (ulong)width * height * depth;
                        valid = width >= 1 && width <= maxAtlasSize && height >= 1 && height <= maxAtlasSize && depth >= 1 && depth <= maxAtlasSize
                            && result.Value == Math.Min(volume, gpuPackingNoBound - 1) && result.Value >= _paddedVolume;
                    }
                    if (!valid) {
                        throw new GpuPackingException("invalid results", $"The GPU returned an impossible result for candidate {i} of {_pendingCount}: status {result.Status}, " +
                            $"atlas {result.Extent & 0xffffu} x {result.Extent >> 16} x {result.Depth}, volume {result.Value:N0} (blocks need at least {_paddedVolume:N0}). " +
                            "The GPU returns only atlas sizes; block positions are checked on the CPU.");
                    }
                }
                // Time spent yielding to the editor only makes the estimate larger, so it may shrink the dispatches but not grow them.
                double millisecondsPerGroup = _sinceSubmit.Elapsed.TotalMilliseconds / _pendingCount;
                if (_yieldedSinceSubmit && millisecondsPerGroup <= _millisecondsPerGroup) return;
                _millisecondsPerGroup = millisecondsPerGroup;
                int groups = (int)Math.Min(int.MaxValue, gpuPackingTargetDispatchMilliseconds / Math.Max(millisecondsPerGroup, 1e-6));
                _groupsPerDispatch = Math.Max(gpuPackingMinGroupsPerDispatch, Math.Min(Math.Min(gpuPackingMaxGroupsPerDispatch, _groupsPerDispatch * 4), groups));
            }

            public void Dispose() {
                AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
                _sizeBuffer?.Release();
                _candidateBuffer?.Release();
                _orderBuffer?.Release();
                _resultBuffer?.Release();
                _sizeBuffer = _candidateBuffer = _orderBuffer = _resultBuffer = null;
            }
        }
    }
}
#endif
