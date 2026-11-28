// A body (D22, docs/design/minigames.md): a thrown or rolled thing the core steps a cell at a time, golf's ball the
// first. Its cell, the way it goes (0 north, 1 east, 2 south, 3 west; a mode may keep -1 for not yet), the cells it has left to roll and to
// carry through the air, and its age in ticks since it was launched. All whole numbers. A mode owns it (its Bodies),
// saves it in its slot and answers its events; World.Bodies.cs steps it.
namespace Chem
{
    public sealed class Body
    {
        public int X, Y, Dir, Roll, Carry, Age;

        public bool Moving => Roll > 0 || Carry > 0;

        public void Launch(int dir, int roll, int carry)
        {
            Dir = dir;
            Roll = roll;
            Carry = carry;
            Age = 0;
        }

        public Body Copy() => (Body)MemberwiseClone();
    }
}
