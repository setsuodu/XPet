using System;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace XPet;

/// <summary>
/// 简单语音播放。语音文件放在 Resources/Voice/ 下（见 put_voice_here.txt）。
/// 文件不存在时静默跳过，不影响主流程。
/// </summary>
public sealed class VoicePlayer
{
    readonly MediaPlayer _player = new();
    readonly string _dir;
    bool _enabled = true;
    double _volume = 1.0;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>0.0 ~ 1.0</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0.0, 1.0);
            _player.Volume = _volume;
        }
    }

    public VoicePlayer()
    {
        // 优先用输出目录旁的 Voice 文件夹（方便热替换），找不到再回退到程序目录
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Resources", "Voice"),
            Path.Combine(AppContext.BaseDirectory, "Voice"),
            Path.Combine(Store.Dir, "Voice"), // %APPDATA%\XPet\Voice 也支持
        };
        _dir = candidates.FirstOrDefault(Directory.Exists)
               ?? Path.Combine(AppContext.BaseDirectory, "Resources", "Voice");
        _player.Volume = _volume;
    }

    /// <summary>播放指定名字的语音（不带扩展名）。支持 .wav / .mp3 / .m4a</summary>
    public void Play(string name)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var path = Resolve(name);
            if (path == null) return;

            _player.Stop();
            _player.Volume = _volume;
            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Play();
        }
        catch
        {
            // 播放失败直接吞掉，不打扰用户
        }
    }

    string? Resolve(string name)
    {
        foreach (var ext in new[] { ".wav", ".mp3", ".m4a", ".ogg" })
        {
            var p = Path.Combine(_dir, name + ext);
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
