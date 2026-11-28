// The defense field's state (docs/BACKLOG.md 17): where the waves are, the lives left and the field's gate, flag and
// bounds. DefenseSystem.cs owns it, with its text and save; Defense.cs keeps the rules' rows and the command.
namespace Chem
{
    public enum DefensePhase
    {
        Idle,
        Running,
        Won,
        Lost,
    }

    public sealed class DefenseState
    {
        public DefensePhase Phase;
        public int Wave, Lives, ToSpawn, NextSpawn, Spawned;
        public int GateX, GateY, FlagX, FlagY;
        public int X0, Y0, X1, Y1;
    }
}
