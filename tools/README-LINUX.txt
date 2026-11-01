Spell Chemistry Valley, alpha {VERSION} for Linux

This plays online with the Windows alpha {VERSION}.

To run it (64-bit x86 Linux desktop):

  unzip SpellChemistryValley-linux-x64.zip -d SpellChemistryValley
  cd SpellChemistryValley
  chmod +x SpellChemistryValley.x86_64
  ./SpellChemistryValley.x86_64

It needs a working Vulkan driver (on Debian or Ubuntu: mesa-vulkan-drivers, or
your graphics card's own driver; `vulkaninfo --summary` shows whether you have
one). There is no OpenGL fallback: without Vulkan the game won't start.

To play together: one player hosts online from the Esc menu and reads out the
code; the other joins by code. Both must be on {VERSION}.

The game is unsigned, comes as a plain folder, and doesn't update itself. The
first minutes can stutter as new effects are seen for the first time; your
graphics driver remembers them, so later runs are smoother.

The log, if anything goes wrong:
  ~/.config/unity3d/anweather/Spell Chemistry Valley/Player.log
