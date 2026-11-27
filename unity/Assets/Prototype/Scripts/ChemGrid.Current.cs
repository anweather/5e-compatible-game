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

    public static CurrentGrid Current => playing != null ? playing.current : null;

    public sealed class CurrentGrid
    {
        readonly ChemGrid grid;

        internal CurrentGrid(ChemGrid grid) => this.grid = grid;

        public World World => grid != null ? grid.World : null;

        public Rules Rules => World?.Rules;

        public void Request(EffectCommand effect) => ChemEffects.Request(grid, effect);

        public ChemGrid Grid => grid;
    }

    void TakeCurrent()
    {
        current ??= new CurrentGrid(this);
        playing = this;
    }

    void LetGoCurrent()
    {
        if (playing == this) playing = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ForgetCurrent() => playing = null;
}
