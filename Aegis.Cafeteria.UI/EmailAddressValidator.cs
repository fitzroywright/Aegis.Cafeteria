namespace Aegis.Cafeteria.UI;

using System.Net.Mail;

internal static class EmailAddressValidator
{
    public static bool TryNormalize(string? value, out string normalizedAddress)
    {
        normalizedAddress = string.Empty;
        string candidate = value?.Trim() ?? string.Empty;
        if (candidate.Length == 0 || candidate.Length > 254)
        {
            return false;
        }

        if (candidate.Any(char.IsControl)
            || candidate.Contains(';')
            || candidate.Contains(','))
        {
            return false;
        }

        try
        {
            MailAddress address = new(candidate);
            if (!string.Equals(address.Address, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int atIndex = address.Address.LastIndexOf('@');
            if (atIndex <= 0 || atIndex >= address.Address.Length - 1)
            {
                return false;
            }

            normalizedAddress = address.Address;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
