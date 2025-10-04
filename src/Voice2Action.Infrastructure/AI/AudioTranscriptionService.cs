using Azure.AI.OpenAI;
using Voice2Action.Domain;

namespace Voice2Action.Infrastructure.AI;

public sealed class OpenAIAudioTranscriptionService : ITranscriptionService
{
    private readonly AzureOpenAIClient _client;
    private readonly string _deployment;

    public OpenAIAudioTranscriptionService(AzureOpenAIClient client, string deployment)
    {
        _client = client;
        _deployment = deployment;
    }

    public async Task<string> TranscribeAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Audio file not found", filePath);

        var audioClient = _client.GetAudioClient(_deployment);
        using var stream = File.OpenRead(filePath);

        // Attempt patterns: (BinaryContent, string, options?, CancellationToken) OR (Stream, ...)
        string? text = null;
        try
        {
            var methods = audioClient
                .GetType()
                .GetMethods()
                .Where(m => m.Name == "TranscribeAudioAsync")
                .ToList();
            var method = methods.OrderByDescending(m => m.GetParameters().Length).FirstOrDefault();
            if (method == null)
                throw new MissingMethodException(
                    "Could not locate TranscribeAudioAsync on audio client."
                );

            var parameters = method.GetParameters();
            if (parameters.Length < 2)
                throw new NotSupportedException(
                    "TranscribeAudioAsync method has unexpected parameter count."
                );

            object firstArg;
            var firstParamType = parameters[0].ParameterType;
            if (firstParamType.Name == "BinaryContent")
            {
                // Try to construct BinaryContent from stream or bytes via reflection.
                firstArg =
                    CreateBinaryContent(firstParamType, stream)
                    ?? throw new InvalidOperationException(
                        "Failed to construct BinaryContent for audio stream."
                    );
            }
            else if (typeof(Stream).IsAssignableFrom(firstParamType))
            {
                firstArg = stream;
            }
            else
            {
                throw new NotSupportedException(
                    $"Unsupported first parameter type for TranscribeAudioAsync: {firstParamType.FullName}"
                );
            }

            var fileName = Path.GetFileName(filePath);
            object?[] args;
            if (parameters.Length == 4)
            {
                // Assume (audio, filename, options, ct)
                args = new object?[] { firstArg, fileName, null, ct };
            }
            else if (parameters.Length == 3)
            {
                // Could be (audio, filename, ct)
                if (parameters[2].ParameterType == typeof(CancellationToken))
                    args = new object?[] { firstArg, fileName, ct };
                else
                    args = new object?[] { firstArg, fileName, null }; // hope token omitted
            }
            else if (parameters.Length == 5)
            {
                // (audio, filename, ???, options, ct) -> best effort with nulls
                args = new object?[] { firstArg, fileName, null, null, ct };
            }
            else
            {
                throw new NotSupportedException(
                    "Unsupported TranscribeAudioAsync parameter shape."
                );
            }

            dynamic task = method.Invoke(audioClient, args)!;
            await task; // await Task<T>
            var resultProp = task.GetType().GetProperty("Result");
            var resultVal = resultProp?.GetValue(task);
            if (resultVal != null)
            {
                var valueProp = resultVal.GetType().GetProperty("Value");
                var inner = valueProp?.GetValue(resultVal) ?? resultVal;
                var textProp = inner.GetType().GetProperty("Text");
                text = textProp?.GetValue(inner) as string;
            }
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("Audio transcription invocation failed.", e);
        }
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Transcription returned empty text.");
        return text;
    }

    private static object? CreateBinaryContent(Type binaryContentType, Stream stream)
    {
        // Try constructor(Stream)
        var ctor = binaryContentType
            .GetConstructors()
            .FirstOrDefault(c =>
            {
                var ps = c.GetParameters();
                return ps.Length == 1 && typeof(Stream).IsAssignableFrom(ps[0].ParameterType);
            });
        if (ctor != null)
        {
            try
            {
                return ctor.Invoke(new object[] { stream });
            }
            catch
            { /* ignore */
            }
        }
        // Try constructor(byte[])
        var ctorBytes = binaryContentType
            .GetConstructors()
            .FirstOrDefault(c =>
            {
                var ps = c.GetParameters();
                return ps.Length == 1 && ps[0].ParameterType == typeof(byte[]);
            });
        if (ctorBytes != null)
        {
            try
            {
                stream.Position = 0;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ctorBytes.Invoke(new object[] { ms.ToArray() });
            }
            catch
            { /* ignore */
            }
            finally
            {
                if (stream.CanSeek)
                    stream.Position = 0;
            }
        }
        // Look for static Create/From methods
        var staticFactory = binaryContentType
            .GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
            )
            .FirstOrDefault(m =>
                m.GetParameters().Length == 1
                && (m.Name is "Create" or "FromStream" or "From")
                && typeof(Stream).IsAssignableFrom(m.GetParameters()[0].ParameterType)
            );
        if (staticFactory != null)
        {
            try
            {
                return staticFactory.Invoke(null, new object[] { stream });
            }
            catch
            { /* ignore */
            }
        }
        return null;
    }
}
