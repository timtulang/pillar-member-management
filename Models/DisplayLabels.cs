namespace Pillar.Models;

public static class DisplayLabels
{
    public static string Text(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Not provided";
        if (value == "non_binary") return "Non-binary";
        if (value == "qr_scan") return "QR scan";
        var text = value.Trim().Replace('_', ' ').ToLowerInvariant();
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
