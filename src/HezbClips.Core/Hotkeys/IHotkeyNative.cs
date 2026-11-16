namespace HezbClips.Core.Hotkeys;

public interface IHotkeyNative
{
    bool Register(int id, uint modifiers, uint vk);
    void Unregister(int id);
}

public record HotkeyFailure(string Name, string Combo);
