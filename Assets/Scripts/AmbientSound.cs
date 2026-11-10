using UnityEngine;

/// <summary>
/// Background ambience for the village: soft wind with slow gusts,
/// rustling leaves and birds chirping now and then. Plays in the menu,
/// mono mode and VR mode.
///
/// The sound is generated in code when the game starts, so no audio file
/// is needed. To use a real recording instead, either drag it onto
/// "Custom Clip" on the camera, or put it in Assets/Resources and name it
/// "AmbientSound" (e.g. Assets/Resources/AmbientSound.mp3).
///
/// This component adds itself to the scene camera when the game starts,
/// so it does not have to be attached by hand.
/// </summary>
public class AmbientSound : MonoBehaviour
{
    [Range(0f, 1f)] public float volume = 0.5f;

    [Tooltip("Optional real recording to play instead of the generated sound.")]
    public AudioClip customClip;

    [Header("Generated sound")]
    [Range(0f, 1f)] public float windLevel = 0.6f;
    [Range(0f, 1f)] public float leavesLevel = 0.25f;
    [Range(0f, 1f)] public float birdLevel = 0.35f;
    [Tooltip("Bird calls per minute.")]
    public float birdCallsPerMinute = 16f;

    const int SampleRate = 22050;
    const float LoopSeconds = 30f;
    const float CrossfadeSeconds = 1.5f;

    private AudioSource source;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AttachToSceneCamera()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (!sceneName.StartsWith("Assignment2") || Object.FindAnyObjectByType<AmbientSound>() != null)
            return;

        AudioListener listener = Object.FindAnyObjectByType<AudioListener>();
        GameObject target = listener != null ? listener.gameObject : Camera.main != null ? Camera.main.gameObject : null;
        if (target != null)
            target.AddComponent<AmbientSound>();
    }

    void Start()
    {
        AudioClip clip = customClip;
        if (clip == null)
            clip = Resources.Load<AudioClip>("AmbientSound");
        if (clip == null)
            clip = GenerateAmbience();

        source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.volume = volume;
        source.spatialBlend = 0f; // all around the player, not from one spot
        source.playOnAwake = false;
        source.Play();
    }

    void Update()
    {
        if (source != null)
            source.volume = volume;
    }

    // ------------------------------------------------------------------
    // Sound generation
    // ------------------------------------------------------------------
    AudioClip GenerateAmbience()
    {
        int loopSamples = Mathf.RoundToInt(LoopSeconds * SampleRate);
        int fadeSamples = Mathf.RoundToInt(CrossfadeSeconds * SampleRate);

        // Generate a little extra and blend it over the start, so the loop has no click.
        float[] raw = new float[loopSamples + fadeSamples];
        var random = new System.Random(806);

        AddWindAndLeaves(raw, random);
        AddBirds(raw, random);

        float[] data = new float[loopSamples];
        for (int i = 0; i < loopSamples; i++)
        {
            data[i] = raw[i];
            if (i < fadeSamples)
            {
                float t = (float)i / fadeSamples;
                data[i] = raw[i] * t + raw[loopSamples + i] * (1f - t);
            }
        }

        Normalize(data, 0.8f);

        AudioClip clip = AudioClip.Create("Village Ambience", loopSamples, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void AddWindAndLeaves(float[] buffer, System.Random random)
    {
        float brown = 0f, windLow = 0f, whoosh1 = 0f, whoosh2 = 0f, leavesLow = 0f, leavesHigh = 0f;
        float twoPi = Mathf.PI * 2f;

        for (int i = 0; i < buffer.Length; i++)
        {
            float time = (float)i / SampleRate;
            float white = (float)(random.NextDouble() * 2.0 - 1.0);

            // ---- Wind: deep, smooth noise that swells in slow gusts ----
            brown = (brown + 0.02f * white) * 0.998f;
            windLow += (brown - windLow) * 0.05f;

            // Gust speeds fit a whole number of times into the loop, so it repeats smoothly.
            float gust = 0.55f
                + 0.25f * Mathf.Sin(twoPi * time * 2f / LoopSeconds)
                + 0.15f * Mathf.Sin(twoPi * time * 5f / LoopSeconds + 1.3f)
                + 0.05f * Mathf.Sin(twoPi * time * 11f / LoopSeconds + 0.4f);

            // Phone speakers barely play deep sounds, so add a higher "whoosh" too.
            whoosh1 += (white - whoosh1) * 0.12f;
            whoosh2 += (whoosh1 - whoosh2) * 0.12f;

            float wind = (windLow * 1.5f + whoosh2 * 3f) * gust;

            // ---- Leaves: soft high hiss that rustles more in strong gusts ----
            leavesLow += (white - leavesLow) * 0.35f;
            leavesHigh = white - leavesLow;
            float rustle = Mathf.Clamp01((gust - 0.55f) * 2.5f);
            float leaves = leavesHigh * 0.6f * rustle * rustle;

            buffer[i] += wind * windLevel + leaves * leavesLevel;
        }
    }

    void AddBirds(float[] buffer, System.Random random)
    {
        int calls = Mathf.RoundToInt(birdCallsPerMinute * LoopSeconds / 60f);

        for (int call = 0; call < calls; call++)
        {
            float start = (float)(random.NextDouble() * (LoopSeconds - 2f));
            float loudness = 0.4f + (float)random.NextDouble() * 0.6f; // near and far birds

            if (random.NextDouble() < 0.6)
                AddTweets(buffer, random, start, loudness);
            else
                AddTrill(buffer, random, start, loudness);
        }
    }

    // A few short rising or falling "tweet" notes.
    void AddTweets(float[] buffer, System.Random random, float start, float loudness)
    {
        int notes = 2 + random.Next(4);
        float baseFrequency = 2600f + (float)random.NextDouble() * 1400f;
        bool rising = random.NextDouble() < 0.5;
        float time = start;

        for (int n = 0; n < notes; n++)
        {
            float length = 0.06f + (float)random.NextDouble() * 0.08f;
            float from = rising ? baseFrequency : baseFrequency * 1.45f;
            float to = rising ? baseFrequency * 1.45f : baseFrequency;
            AddChirp(buffer, time, length, from, to, 0f, 0f, loudness);
            time += length + 0.05f + (float)random.NextDouble() * 0.12f;
        }
    }

    // A fast warbling trill.
    void AddTrill(float[] buffer, System.Random random, float start, float loudness)
    {
        float length = 0.4f + (float)random.NextDouble() * 0.5f;
        float frequency = 3200f + (float)random.NextDouble() * 900f;
        float warbleSpeed = 22f + (float)random.NextDouble() * 12f;
        AddChirp(buffer, start, length, frequency, frequency * 0.9f, warbleSpeed, 350f, loudness * 0.8f);
    }

    void AddChirp(float[] buffer, float startTime, float length, float fromHz, float toHz,
        float warbleHz, float warbleDepthHz, float loudness)
    {
        int first = Mathf.RoundToInt(startTime * SampleRate);
        int count = Mathf.RoundToInt(length * SampleRate);
        float phase = 0f;

        for (int i = 0; i < count && first + i < buffer.Length; i++)
        {
            float t = (float)i / count;
            float seconds = (float)i / SampleRate;

            float frequency = Mathf.Lerp(fromHz, toHz, t)
                + warbleDepthHz * Mathf.Sin(2f * Mathf.PI * warbleHz * seconds);
            phase += 2f * Mathf.PI * frequency / SampleRate;

            // Smooth start and end, plus a quieter overtone for a natural sound.
            float envelope = Mathf.Sin(Mathf.PI * t);
            envelope *= envelope;
            float tone = Mathf.Sin(phase) + 0.25f * Mathf.Sin(2f * phase);

            buffer[first + i] += tone * envelope * loudness * birdLevel * 1.2f;
        }
    }

    static void Normalize(float[] data, float peak)
    {
        float max = 0.0001f;
        foreach (float sample in data)
            max = Mathf.Max(max, Mathf.Abs(sample));

        float scale = peak / max;
        for (int i = 0; i < data.Length; i++)
            data[i] *= scale;
    }
}
