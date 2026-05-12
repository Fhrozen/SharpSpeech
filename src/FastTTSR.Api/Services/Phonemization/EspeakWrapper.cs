using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace FastTTSR.Api.Services.Phonemization;

public sealed class EspeakWrapper : IDisposable
{
    private enum Error
    {
        EE_OK = 0,
        EE_INTERNAL_ERROR = -1,
        EE_BUFFER_FULL = 1,
        EE_NOT_FOUND = 2,
    }

    private static bool _initialized;
    private static readonly object _initLock = new();
    
    // Thread-safety lock: espeak_TextToPhonemes() returns a pointer to a static global buffer
    // that gets reallocated dynamically. Concurrent calls can invalidate pointers, causing
    // "free(): invalid pointer" crashes, especially with multi-byte UTF-8 text (Japanese, Chinese).
    private static readonly SemaphoreSlim _espeakLock = new(1, 1);
    
    private bool _disposed;

    public EspeakWrapper(string? dataPath = null)
    {
        Initialize(dataPath);
    }

    private static void Initialize(string? dataPath)
    {
        lock (_initLock)
        {
            if (_initialized)
            {
                return;
            }

            // Default paths to check for espeak-ng-data
            dataPath ??= ResolveEspeakDataPath();

            var result = espeak_Initialize(0x02, 0, dataPath, 0);
            if (result == (int)Error.EE_INTERNAL_ERROR)
            {
                throw new Exception($"Could not initialize Espeak. Data path: {dataPath}");
            }

            _initialized = true;
            Console.WriteLine($"Espeak initialized with data path: {dataPath}");
        }
    }

    private static string ResolveEspeakDataPath()
    {
        // Check environment variable first
        var envPath = Environment.GetEnvironmentVariable("ESPEAK_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(envPath) && Directory.Exists(envPath))
        {
            return envPath;
        }

        // Check common locations
        var possiblePaths = new[]
        {
            "/app/assets/espeak-ng-data",
            "/assets/espeak-ng-data",
            Path.Combine(AppContext.BaseDirectory, "assets", "espeak-ng-data"),
            "/usr/share/espeak-ng-data",
            "/usr/lib/x86_64-linux-gnu/espeak-ng-data"
        };

        foreach (var path in possiblePaths)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        // Fallback
        return "/app/assets/espeak-ng-data";
    }

    public bool SetVoice(string voice)
    {
        if (!_initialized)
        {
            return false;
        }

        // Thread-safe: espeak voice setting modifies internal state
        _espeakLock.Wait();
        try
        {
            var result = espeak_SetVoiceByName(voice);
            return result == Error.EE_OK;
        }
        finally
        {
            _espeakLock.Release();
        }
    }

    public string? ConvertToPhonemes(string text)
    {
        if (!_initialized || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Normalize whitespace while preserving UTF-8 text
        text = Regex.Replace(text, @"\s+", " ").Trim();

        var phonemes = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var wordCopy = word;
            // Capture trailing punctuation and symbol marks (currency/math/emojis) so the core word is safely phonemized.
            var punctuation = Regex.Match(wordCopy, @"[\p{P}\p{S}]*$").Value;
            
            // Remove trailing punctuation for phoneme conversion
            if (!string.IsNullOrEmpty(punctuation))
            {
                wordCopy = wordCopy[..^punctuation.Length];
            }

            if (!string.IsNullOrWhiteSpace(wordCopy))
            {
                try
                {
                    // CRITICAL: espeak_TextToPhonemes() returns a pointer to a static buffer that can
                    // be reallocated by concurrent calls. We MUST hold the lock from the espeak call
                    // through the Marshal.PtrToStringUTF8 copy to prevent pointer invalidation.
                    _espeakLock.Wait();
                    try
                    {
                        // textmode: espeakCHARS_UTF8=1, phoneme_mode: 0x02 (IPA)
                        var result = espeak_TextToPhonemes(ref wordCopy, 1, 0x02);
                        var phoneme = Marshal.PtrToStringUTF8(result)?.Replace("_", "");
                        phonemes += phoneme;
                    }
                    finally
                    {
                        _espeakLock.Release();
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Espeak phoneme conversion failed for word '{wordCopy}': {ex.Message}");
                    // Skip problematic word
                }
            }

            phonemes += punctuation + " ";
        }

        return phonemes.TrimEnd();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Note: espeak doesn't provide a cleanup function, so just mark as disposed
        _disposed = true;
    }

    // P/Invoke declarations for Linux libespeak-ng.so.1
    private const string DLLName = "libespeak-ng.so.1";

    [DllImport(DLLName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int espeak_Initialize(int output, int bufferLength, string? path, int options);

    [DllImport(DLLName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern Error espeak_SetVoiceByName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(DLLName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr espeak_TextToPhonemes(
        [MarshalAs(UnmanagedType.LPUTF8Str)] ref string text,
        int textmode,
        int phoneme_mode
    );
}
