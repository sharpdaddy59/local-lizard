# Piper Installation Guide for LocalLizard

## Overview

Piper is a fast, local neural text-to-speech system developed by the Rhasspy team. This guide covers installation options for use with the LocalLizard project.

## Installation Options

### Option 1: Pre-built Binary (Recommended)

1. Download the latest Piper binary from the [GitHub releases page](https://github.com/rhasspy/piper/releases)
2. Extract the archive:
   ```bash
   tar -xzf piper_linux_amd64.tar.gz
   ```
3. Move to a directory in your PATH or update the `LizardConfig.PiperPath`:
   ```bash
   mv piper /home/wily/dev/piper/
   chmod +x /home/wily/dev/piper/piper
   ```

### Option 2: Python Package

If you have Python installed, you can use the Python package:

```bash
pip install piper-tts
```

The executable will be installed to `~/.local/bin/piper`. Update `LizardConfig.PiperPath` accordingly.

### Option 3: Build from Source

1. Install dependencies:
   ```bash
   sudo apt-get update
   sudo apt-get install -y cmake build-essential libsndfile1-dev
   ```

2. Clone and build:
   ```bash
   git clone https://github.com/rhasspy/piper.git
   cd piper
   mkdir build && cd build
   cmake ..
   make -j$(nproc)
   ```

3. The binary will be at `src/piper`. Copy it to your desired location.

## Voice Models

Piper requires voice models in ONNX format. Download models from:

- [Official Piper voice models](https://github.com/rhasspy/piper/releases/tag/v0.0.2)
- [Hugging Face Piper models](https://huggingface.co/rhasspy/piper-voices/tree/main)

### Recommended Models for Testing

1. **English - Amy (Medium quality)**: `en_US-amy-medium.onnx`
   - Good balance of quality and speed
   - Approximately 50MB

2. **English - Jenny (High quality)**: `en_US-jenny-medium.onnx`
   - Higher quality, slightly larger
   - Approximately 90MB

### Download Example

```bash
# Create directory for models
mkdir -p /home/wily/dev/piper/models

# Download a model
wget -O /home/wily/dev/piper/models/en_US-amy-medium.onnx \
  https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/amy/medium/en_US-amy-medium.onnx

# Update configuration
export LIZARD_PIPER_MODEL="/home/wily/dev/piper/models/en_US-amy-medium.onnx"
```

## Configuration

Update your `LizardConfig` or set environment variables:

```csharp
// In code
var config = new LizardConfig
{
    PiperPath = "/path/to/piper",
    PiperModel = "/path/to/model.onnx"
};

// Or via environment variables
export LIZARD_PIPER_PATH="/home/wily/dev/piper/piper"
export LIZARD_PIPER_MODEL="/home/wily/dev/piper/models/en_US-amy-medium.onnx"
```

## Verification

Test your installation:

```bash
# Check if piper works
piper --help

# Test synthesis
echo "Hello, world!" | piper -m /path/to/model.onnx -f test.wav

# Play the audio (if you have aplay installed)
aplay test.wav
```

## Integration with LocalLizard

The `PiperTTSService` class automatically validates installation on first use. You can also manually check:

```csharp
using var piperService = new PiperTTSService(config);
var isInstalled = await piperService.ValidateInstallationAsync();

if (isInstalled)
{
    Console.WriteLine("Piper is ready to use!");
}
else
{
    Console.WriteLine("Piper installation validation failed.");
    Console.WriteLine($"Executable path: {config.PiperPath}");
    Console.WriteLine($"Model path: {config.PiperModel}");
}
```

## Troubleshooting

### Common Issues

1. **"Piper executable not found"**
   - Verify the file exists at the configured path
   - Check file permissions: `chmod +x /path/to/piper`
   - Ensure it's executable: `ls -la /path/to/piper`

2. **"Piper model not found"**
   - Download a voice model (see above)
   - Update the model path in configuration
   - Verify the file is readable

3. **Permission denied**
   - Run with appropriate permissions
   - Check SELinux/AppArmor if applicable

4. **Missing dependencies**
   - For pre-built binaries: `ldd /path/to/piper` to check missing libraries
   - Install required system libraries

### Performance Tips

1. **Model size**: Smaller models are faster but lower quality
2. **CPU threads**: Piper can use multiple threads (check `piper --help` for options)
3. **Batch processing**: For multiple texts, reuse the same `PiperTTSService` instance

## Advanced Usage

### Voice Parameters

Piper supports various voice parameters. You can extend `PiperTTSService.BuildPiperArguments()` to include:

```csharp
// Add to arguments list
args.Add("--length-scale 1.0");    // Speaking rate
args.Add("--noise-scale 0.667");   // Voice variation
args.Add("--noise-w 0.8");         // Breathiness
```

### Streaming Audio

For real-time applications, use the streaming API:

```csharp
using var session = await piperService.StartStreamingSessionAsync();
var audioChunk = await session.SynthesizeChunkAsync("Hello");
// Play audioChunk immediately
```

### Multiple Voices

To support multiple voices, create multiple `PiperTTSService` instances with different model paths:

```csharp
var maleVoiceConfig = new LizardConfig 
{ 
    PiperPath = config.PiperPath,
    PiperModel = "/path/to/male-voice.onnx"
};

var femaleVoiceConfig = new LizardConfig 
{ 
    PiperPath = config.PiperPath,
    PiperModel = "/path/to/female-voice.onnx"
};
```

## Resources

- [Piper GitHub Repository](https://github.com/rhasspy/piper)
- [Piper Voice Samples](https://rhasspy.github.io/piper-samples/)
- [Hugging Face Models](https://huggingface.co/rhasspy/piper-voices)
- [LocalLizard PiperTTSService Documentation](../src/LocalLizard.Voice/PiperTTSService.cs)