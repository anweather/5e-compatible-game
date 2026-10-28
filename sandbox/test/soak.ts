import { RULES } from "../src/rules.ts";

/** Runs a test of the flow alone with soaking off (sim.water.soakPass 0), as ChemistryTests.NoSoak does. */
export function withoutSoaking(run: () => void) {
  const pass = RULES.sim.water.soakPass;
  RULES.sim.water.soakPass = 0;
  try {
    run();
  } finally {
    RULES.sim.water.soakPass = pass;
  }
}
