using System.Numerics;
using SerbleAPI.Data.Schemas;

namespace SerbleAPI.Data;

/// <summary>
/// How a user's sign-in flows are evaluated. A flow is a set of methods, stored as a bitmask; a
/// sign-in completes once every method of any one flow has been completed, in any order.
/// </summary>
public static class LoginFlowRules {
    public const int MaxFlows = 8;

    public static bool IsSatisfied(IEnumerable<int> flows, int completed) =>
        flows.Any(f => f != 0 && (f & completed) == f);

    /// <summary>
    /// The methods that may be attempted next. A one-time code in a flow that also has a password or
    /// passkey is held back until one of those is done, so the code cannot be guessed on its own.
    /// </summary>
    public static int Attemptable(IEnumerable<int> flows, int completed) {
        int result = 0;
        foreach (int flow in flows) {
            if (flow == 0 || (completed & ~flow) != 0) continue;

            int candidates = flow & ~completed;
            int nonCode = flow & ~CredentialTypes.OneTimeCodeMask;
            if (nonCode != 0 && (completed & nonCode) == 0) candidates &= ~CredentialTypes.OneTimeCodeMask;
            result |= candidates;
        }
        return result;
    }

    /// <summary>Whether another flow needs a strict subset of <paramref name="flow"/>'s methods, so it is never used.</summary>
    public static bool IsRedundant(IEnumerable<int> flows, int flow) =>
        flows.Any(other => other != 0 && other != flow && (flow & other) == other);

    /// <summary>The flows that only use methods in <paramref name="activeTypes"/>.</summary>
    public static int[] PruneUnsatisfiable(IEnumerable<int> flows, int activeTypes) =>
        flows.Where(f => f != 0 && (f & ~activeTypes) == 0).ToArray();

    /// <summary>Null if <paramref name="flows"/> is an acceptable set, otherwise why not.</summary>
    public static string? Validate(IReadOnlyList<int> flows, int activeTypes) {
        if (flows.Count == 0) return "At least one sign-in flow is required";
        if (flows.Count > MaxFlows) return $"No more than {MaxFlows} sign-in flows are allowed";

        foreach (int flow in flows) {
            if (flow == 0) return "A sign-in flow needs at least one method";
            if ((flow & ~CredentialTypes.KnownMask) != 0) return "Unknown sign-in method";
            if ((flow & ~activeTypes) != 0) return "A sign-in flow uses a method that has not been set up";
        }

        if (flows.Distinct().Count() != flows.Count) return "Duplicate sign-in flow";

        // A code-only flow of several codes lets one of them be entered first, and that code then
        // counts towards any mixed flow it is also in, ahead of the password or passkey. A flow of a
        // single code does not: entering it completes the sign-in outright.
        int codesAlone = 0;
        int codesMixed = 0;
        foreach (int flow in flows) {
            int codes = flow & CredentialTypes.OneTimeCodeMask;
            if ((flow & ~CredentialTypes.OneTimeCodeMask) != 0) codesMixed |= codes;
            else if (BitOperations.PopCount((uint)codes) > 1) codesAlone |= codes;
        }
        if ((codesAlone & codesMixed) != 0) {
            return "A code method cannot be used both without and alongside a password or passkey";
        }

        return null;
    }
}
