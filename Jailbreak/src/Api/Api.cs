using Jailbreak.Contract;

namespace Jailbreak;

public sealed class Api : IJailbreak
{
    public IJBPlayerManagement Players { get; }

    public Api(IJBPlayerManagement playerManagement)
    {
        Players = playerManagement;
    }
}