Needs: core
Area: unity/Assets/Prototype/Chemistry/ (named acts, Places), their tests
Branch: named-acts-149-150

# Named acts: #149 and #150

Read issues #149 ("a firing that drops rows says it's done") and #150 ("rows don't share a spot at a named place").
Find the named-acts code (grep `act` in Chemistry/ and `rules/`, and the issues' own notes). Fix both in the core,
with tests that fail before and pass after: a firing that drops rows must not report done; rows at a named place
must share the spot as the issue expects. Deterministic: integer state, seeded RNG, ordered iteration, no Unity.
If either needs a design call (what "share a spot" means), write the question and the two options in the report
and do the other one. Goldens unchanged unless a fix must change them: say which. Don't touch Scripts/.
Done when build -warnaserror, `dotnet test core` and `dotnet format core --verify-no-changes` pass; push the branch;
report commit and test counts. The PC compiles it.
