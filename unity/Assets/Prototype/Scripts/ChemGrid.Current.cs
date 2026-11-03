using Chem;
using UnityEngine;

// The grid in play, without searching the scene for it: ChemGrid.Current.
// It shows only what most of the view needs from the grid: its world, the
// rules in force and the door for effects (ChemEffects.Request). Code that
// draws on the grid (cell positions, heights, its events) takes the grid
// itself from Current.Grid.
//
// Current is the grid whose Awake ran last and that isn't destroyed. It is
// null before that Awake, once that grid is destroyed (a scene reload: the
// new grid's Awake sets it again), outside Play mode (EditMode tests and the
// Editor builders: a grid in an open scene never ran its Awake there), and in
// a fresh Play mode (the project doesn't reload the domain, so it's cleared
// at start). A world reset keeps it: World is always the grid's current one.
// A Multiplayer Play Mode virtual player is a process of its own with its own.
public partial class ChemGrid
{
    static ChemGrid playing;
    CurrentGrid current;

    /// <summary>
    /// The grid in play, or null when there is none (see the top of the file). It never hands out a
    /// destroyed grid; keep the old null checks where it's read.
    /// </summary>
    public static CurrentGrid Current => playing != null ? playing.current : null;

    /// <summary>The narrow face of the grid in play: its world, the rules in force and the effect door.</summary>
    public sealed class CurrentGrid
    {
        readonly ChemGrid grid;

        internal CurrentGrid(ChemGrid grid) => this.grid = grid;

        /// <summary>The grid's world (a new one after each ResetWorld), or null before it's built or once the grid is gone.</summary>
        public World World => grid != null ? grid.World : null;

        /// <summary>The rules in force in that world, or null when there is no world.</summary>
        public Rules Rules => World?.Rules;

        /// <summary>Asks for an effect on the world, the way ChemEffects.Request does: applied solo, stamped by the host online.</summary>
        public void Request(EffectCommand effect) => ChemEffects.Request(grid, effect);

        /// <summary>The grid itself, for code that draws on it (positions, heights, events).</summary>
        public ChemGrid Grid => grid;
    }

    // First thing in Awake, before the world is built: code that found the grid by searching found it then too.
    void TakeCurrent()
    {
        current ??= new CurrentGrid(this);
        playing = this;
    }

    // In OnDestroy. Only the grid in play lets go, so an old grid destroyed after the new one woke keeps the new one.
    void LetGoCurrent()
    {
        if (playing == this) playing = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ForgetCurrent() => playing = null;
}
