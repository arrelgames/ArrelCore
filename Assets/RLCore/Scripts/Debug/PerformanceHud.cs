using System.Text;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RLGames
{
    [DisallowMultipleComponent]
    public sealed class PerformanceHud : MonoBehaviour
    {
        [SerializeField] private Text metricsText;
        [SerializeField] private Text averageText;
        [Min(0.05f)]
        [SerializeField] private float updateInterval = 0.25f;
        [Min(1)]
        [SerializeField] private int fpsSampleCount = 60;
        [Min(0.5f)]
        [SerializeField] private float averageWindowSeconds = 3f;
        [SerializeField] private KeyCode toggleKey = KeyCode.F3;

        private readonly StringBuilder sb = new StringBuilder(160);
        private float elapsed;
        private float fpsAccum;
        private int fpsFrames;
        private float updateTimer;
        private bool visible = true;
        private readonly Queue<HudSample> averageSamples = new Queue<HudSample>(128);
        private float sumAvgFps;
        private float sumAvgFrameMs;
        private float sumAvgCpuMs;
        private float sumAvgGpuMs;
        private int sumAvgGpuCount;

        private ProfilerRecorder mainThreadRecorder;
        private bool mainThreadRecorderValid;

        private struct HudSample
        {
            public float time;
            public float fps;
            public float frameMs;
            public float cpuMs;
            public float gpuMs;
            public bool gpuValid;
        }

        private void Awake()
        {
            if (metricsText == null)
                metricsText = GetComponentInChildren<Text>(true);
            if (averageText == null)
            {
                Text[] texts = GetComponentsInChildren<Text>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i] != metricsText)
                    {
                        averageText = texts[i];
                        break;
                    }
                }
            }
        }

        private void OnEnable()
        {
            TryStartRecorders();
            SetVisible(visible);
        }

        private void OnDisable()
        {
            if (mainThreadRecorder.Valid)
                mainThreadRecorder.Dispose();
            mainThreadRecorderValid = false;
        }

        private void Update()
        {
            if (IsTogglePressed())
                SetVisible(!visible);

            float dt = Mathf.Max(0.000001f, Time.unscaledDeltaTime);
            elapsed += dt;
            fpsFrames++;
            fpsAccum += 1f / dt;
            if (fpsFrames > Mathf.Max(1, fpsSampleCount))
            {
                fpsFrames = fpsSampleCount;
                fpsAccum *= (fpsSampleCount - 1f) / fpsSampleCount;
            }

            updateTimer += dt;
            if (updateTimer < updateInterval)
                return;
            updateTimer = 0f;

            if (metricsText == null || !visible)
                return;

            float avgFps = fpsFrames > 0 ? fpsAccum / fpsFrames : 0f;
            float frameMs = avgFps > 0.001f ? 1000f / avgFps : 0f;
            float cpuMs = GetCpuFrameMs(frameMs);
            float gpuMs = GetGpuFrameMs();
            AddAverageSample(avgFps, frameMs, cpuMs, gpuMs);

            sb.Length = 0;
            int sampleCount = Mathf.Max(1, averageSamples.Count);
            float avgWindowFps = sumAvgFps / sampleCount;
            float avgWindowCpuMs = sumAvgCpuMs / sampleCount;

            sb.Append("FPS: ").Append(avgFps.ToString("0.0")).Append('\n');
            sb.Append("Frame: ").Append(avgFps.ToString("0.0")).Append(" fps\n");
            sb.Append("CPU: ").Append(cpuMs.ToString("0.00")).Append(" ms\n");
            sb.Append("GPU: ");
            if (gpuMs >= 0f)
                sb.Append(gpuMs.ToString("0.00")).Append(" ms");
            else
                sb.Append("N/A");

            metricsText.text = sb.ToString();

            if (averageText != null)
            {
                sb.Length = 0;
                sb.Append("AVG: ").Append(avgWindowFps.ToString("0.0")).Append('\n');
                sb.Append("AVG: ").Append(avgWindowFps.ToString("0.0")).Append(" fps\n");
                sb.Append("AVG: ").Append(avgWindowCpuMs.ToString("0.00")).Append(" ms\n");
                sb.Append("AVG: ");
                if (sumAvgGpuCount > 0)
                    sb.Append((sumAvgGpuMs / sumAvgGpuCount).ToString("0.00")).Append(" ms");
                else
                    sb.Append("N/A");
                averageText.text = sb.ToString();
            }
        }

        private void SetVisible(bool isVisible)
        {
            visible = isVisible;
            if (metricsText != null)
                metricsText.gameObject.SetActive(isVisible);
            if (averageText != null)
                averageText.gameObject.SetActive(isVisible);
        }

        private void TryStartRecorders()
        {
            if (mainThreadRecorder.Valid)
                mainThreadRecorder.Dispose();

            mainThreadRecorderValid = false;
            mainThreadRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
            mainThreadRecorderValid = mainThreadRecorder.Valid;
        }

        private float GetCpuFrameMs(float fallbackMs)
        {
            if (!mainThreadRecorderValid || !mainThreadRecorder.Valid)
                return fallbackMs;

            long value = mainThreadRecorder.LastValue;
            if (value <= 0)
                return fallbackMs;

            // ProfilerRecorder values are typically in nanoseconds for frame timing counters.
            return value / 1_000_000f;
        }

        private static float GetGpuFrameMs()
        {
            FrameTimingManager.CaptureFrameTimings();
            FrameTiming[] timings = new FrameTiming[1];
            uint count = FrameTimingManager.GetLatestTimings(1, timings);
            if (count == 0)
                return -1f;

            double gpu = timings[0].gpuFrameTime;
            if (gpu <= 0.0)
                return -1f;

            return (float)gpu;
        }

        private bool IsTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return false;

            switch (toggleKey)
            {
                case KeyCode.F1: return keyboard.f1Key.wasPressedThisFrame;
                case KeyCode.F2: return keyboard.f2Key.wasPressedThisFrame;
                case KeyCode.F3: return keyboard.f3Key.wasPressedThisFrame;
                case KeyCode.F4: return keyboard.f4Key.wasPressedThisFrame;
                case KeyCode.F5: return keyboard.f5Key.wasPressedThisFrame;
                case KeyCode.F6: return keyboard.f6Key.wasPressedThisFrame;
                case KeyCode.F7: return keyboard.f7Key.wasPressedThisFrame;
                case KeyCode.F8: return keyboard.f8Key.wasPressedThisFrame;
                case KeyCode.F9: return keyboard.f9Key.wasPressedThisFrame;
                case KeyCode.F10: return keyboard.f10Key.wasPressedThisFrame;
                case KeyCode.F11: return keyboard.f11Key.wasPressedThisFrame;
                case KeyCode.F12: return keyboard.f12Key.wasPressedThisFrame;
                default: return keyboard.f3Key.wasPressedThisFrame;
            }
#else
            return Input.GetKeyDown(toggleKey);
#endif
        }

        private void AddAverageSample(float fps, float frameMs, float cpuMs, float gpuMs)
        {
            HudSample sample = new HudSample
            {
                time = Time.unscaledTime,
                fps = fps,
                frameMs = frameMs,
                cpuMs = cpuMs,
                gpuMs = gpuMs,
                gpuValid = gpuMs >= 0f
            };

            averageSamples.Enqueue(sample);
            sumAvgFps += sample.fps;
            sumAvgFrameMs += sample.frameMs;
            sumAvgCpuMs += sample.cpuMs;
            if (sample.gpuValid)
            {
                sumAvgGpuMs += sample.gpuMs;
                sumAvgGpuCount++;
            }

            float minTime = Time.unscaledTime - Mathf.Max(0.5f, averageWindowSeconds);
            while (averageSamples.Count > 0 && averageSamples.Peek().time < minTime)
            {
                HudSample old = averageSamples.Dequeue();
                sumAvgFps -= old.fps;
                sumAvgFrameMs -= old.frameMs;
                sumAvgCpuMs -= old.cpuMs;
                if (old.gpuValid)
                {
                    sumAvgGpuMs -= old.gpuMs;
                    sumAvgGpuCount = Mathf.Max(0, sumAvgGpuCount - 1);
                }
            }
        }
    }
}
