# D22. Mini-games are world modes, each its own assembly (2026-11-27)

**Status:** Accepted (Andrew, 2026-11-28: "Accept it")

- **Context:** Andrew wants mini-games (golf now; frisbee, bowling next) to be add-ons: a new one is its own project
  and view folder, touches nothing in Sim, and lands in about a minute running only its own tests. Golf was woven
  into Sim: the world owned `GolfSystem`, and spells, strikes, the dragon, acts, the save and the checksum named its
  ball. A move into `Chem.Play` (architecture-review step 9) kept every hash but stopped on the lockstep tests' helpers
  (only in `Chem.Lockstep.Tests`), the rules MCP's rooms (compiled into Game.Core) placing golf, and a mode's state
  having nowhere to live but Sim.
- **Decision:** Sim keeps a contract and generic parts; each mini-game is a mode on them
  (`docs/design/minigames.md`):
  - `WorldMode`, an abstract class in Sim with virtual hooks; the world calls installed modes in install order at
    fixed places. A joiner's world takes the same modes in the same order (`MadeFor`).
  - A slot per mode for save, checksum text and the dragon's start, called where golf's own calls were. A loaded save
    waits for its modes until the first tick.
  - Bodies: Sim steps thrown and rolled bodies (flight, roll, slope, bounce, downhill). Spells, waves and boulders
    strike bodies, never "the ball". The mode that owns a body saves it in its slot and answers its events.
  - Events as hooks: a map laid, a chunk reset by the dragon, a spell striking or landing on a body, a body's
    bounce, arrival, leaving and stop. Acts of a kind Sim doesn't know go to the modes.
  - The rules MCP's rooms get their modes from whoever makes the workbench (`Workbench.Modes`).
  - The late-join harness is a test kit (`LockstepKit`, `core/Chem.Lockstep.Kit`) that any mode's tests use.
  - Golf is the first: `Runtime/Golf/`, the `Chem.Play.Golf` assembly, with its own test project.
  - Not done: the cup as a rules tag. It is a point in golf's state; a tag would change cell data.
- **Why:**
  - A mode's change runs its own tests: the gates for a golf file went from Sim's to golf's own.
  - Nothing about saves or hashes moves. Golf is the only slotted mode, so the bytes are the same. A new mode's slot
    comes after it, so worlds without the new mode keep their bytes.
  - One generic body is what a frisbee and a bowling ball need too, and keeps spells, strikes and the dragon apart
    from any one game.
  - Hooks on a base class, called in install order, are deterministic. An event bus or registration by name would
    need order rules of its own.
- **Since (modes-out, 2026-11-28):** the Hold's, its progress', the defense field's and the course's state moved onto
  the contract too, owned by `PlayModes` with its save, text and hash, and `IWorldModes` is gone. Their bytes keep the
  places they had in the save and the checksums (`SaveAt`, `TextAt`), and the hooks the world asks the Hold as it runs
  (its tick, a moment, the commands, zones, stands, a run's locks and picks) are on `WorldMode`. The raiders and the
  dragon stay in Sim (`docs/design/minigames.md`, "The Hold on it").
- **Reopen if:** a mode needs to act inside a stage the hooks don't reach (add the hook to the contract, as its own
  change with its proof); a save-format change is wanted (Andrew's call); or two modes start needing each other's
  state.
- **Sources:** `docs/design/minigames.md`; `docs/design/architecture-review.md` step 9; golf's move experiment
  (2026-11-27).
