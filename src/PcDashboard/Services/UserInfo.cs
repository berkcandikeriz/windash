using System.Runtime.InteropServices;
using System.Text;

namespace PcDashboard.Services;

/// <summary>Selamlamadaki ad: ayarda yazılan ad, yoksa Windows hesabının görünen adının ilk kelimesi.</summary>
public static class UserInfo
{
    private const int NameDisplay = 3;
    private static string? _windowsFirstName;

    public static string FirstName(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        return _windowsFirstName ??= ReadWindowsFirstName();
    }

    private static string ReadWindowsFirstName()
    {
        try
        {
            // Microsoft / etki alanı hesaplarında "Ad Soyad" döner; yerel hesaplarda başarısız olabilir
            uint size = 256;
            var sb = new StringBuilder((int)size);
            if (GetUserNameEx(NameDisplay, sb, ref size))
            {
                var display = sb.ToString().Trim();
                if (display.Length > 0) return display.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            }
        }
        catch
        {
            // yok say
        }

        var user = Environment.UserName;
        return user.Length > 0 ? char.ToUpper(user[0]) + user[1..] : "";
    }

    [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserNameEx(int nameFormat, StringBuilder userName, ref uint userNameSize);
}
