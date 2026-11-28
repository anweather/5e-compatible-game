// A player's run on the course (docs/BACKLOG.md 25): the checkpoint reached, the clock's start, the best and the last
// time, in ticks. CourseSystem.cs owns them, with their text and save; Course.cs keeps the course's layout and zones.
namespace Chem
{
    public sealed class CourseRun
    {
        public string Player;
        public int Checkpoint = -1, Start = -1, Best, Last;
        public bool Running => Start >= 0;
    }
}
