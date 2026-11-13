# AVATAR-RACE — review and handoff (13/11/2026)

Branch: `fix/avatar-concurrent-upload-race-2`, starting HEAD `5f1ae62`.
Source fix: `d3347ed4c4ff01ebc6f2afbaec58ff49e73368ea`.

## Root cause

This is a production race exposed by the test, not merely an incorrect assertion.
Avatar original and thumbnail keys are fixed per user. Before the fix, requests A and B could both read the account,
then independently write their objects and save their metadata. For example, A writes objects, B overwrites them,
B saves its byte counts, A saves its byte counts last. Both return 200, but the row describes A while storage contains B.
The two object assignments can also interleave. `AssertStored` compares actual object lengths with the row's byte counts,
so it fails depending on scheduling; the original barrier released both writers together but did not control their finish order.

## Fix and critical review

The source commit starts a transaction, locks this account's `app_user` row with parameterized
`SELECT 1 ... FOR UPDATE`, writes both objects, saves all five avatar columns, then commits.
PostgreSQL holds the lock until transaction completion, across requests and API instances. With two successful uploads,
B cannot enter the object store until A has committed; B then replaces the objects and the whole metadata set together
in the same serialization order. `MarkAvatarModified` forces the entire set to be saved even when fixed keys have not changed.
This removes the scheduling-dependent byte-count mismatch after both requests finish. It does not promise that tests
cannot fail due to infrastructure, cancellation, or unrelated faults.

The original commit did **not** re-read after locking. The working-tree follow-up makes the first LINQ lookup
`AsNoTracking()` and performs a tracked LINQ lookup after the lock, before any object write. This follows AGENTS.md's
read → `SELECT 1 ... FOR UPDATE` → re-read rule and detects an account deleted while waiting without writing orphan objects.
Neither lookup bypasses query filters. `AppUser` itself has no commune filter or `ICommuneScoped` marker; this route's
ownership boundary is `OwnUserId` from the authenticated actor, not a request-supplied user ID. No `SELECT *`/`FromSql`
or manual entity materialization is introduced. Exceptions unwind/dispose the transaction; no query follows a caught SQL
error inside it.

Holding the lock across the object write is justified for these existing fixed keys: locking only SaveChanges would still
allow object order and row order to diverge. The client stream is fully read and sanitized (JPEG, ≤1 MiB input, ≤512px
sanitized long edge) **before** taking the lock. This is not a lock across streaming an unbounded client upload or model
inference. Be precise: `StoreImageAsync` still buffers the sanitized image, generates its thumbnail, and performs S3 writes
under the lock. Storage delays therefore delay other updates to this account; that is the cost of this minimal fix.
Versioned object keys could avoid overwrite races but change the established key/storage lifecycle design and are outside
this repair. Objects are still written before the row commits, as required by media-storage.md.

Limits: PostgreSQL cannot roll back object storage. A failed replacement after overwriting a fixed key can still leave
old metadata with new/partial bytes, and concurrent GETs are not snapshot-consistent across DB and S3. These pre-existing
failure/read behaviors are not fixed by this successful-concurrent-upload repair. No API/schema/dependency changes.

## Test changes

The source barrier waited up to 20 seconds and propagated timeout. After serialization, writer one holds the account lock
while waiting for writer two, which cannot reach the hook until writer one commits. The source fix changes the hook to
catch a one-second timeout, allowing the first upload to complete and the second to follow. With the lock present this
timeout is expected, not a failure; the hook no longer requires simultaneous entry. Without the lock, the concurrent
test remains a probabilistic regression detector, so a single green run after removing the lock proves nothing.

Added `AvatarTests.Upload_holds_account_lock_while_writing_objects` for a reliable break signal. During a **single** upload,
the object-store hook uses a separate DbContext/connection to attempt `FOR NO KEY UPDATE NOWAIT` on the same account.
It must receive PostgreSQL `55P03` (`LockNotAvailable`) while the upload owns the lock. NOWAIT avoids timing assertions
and deadlock; NO KEY UPDATE avoids a false positive from incidental FK KEY SHARE locks. The test asserts the hook ran,
the upload returned 200, and the resulting metadata matches the stored objects.

### Exact break test for Claude (Docker required)

Run from this worktree, with Docker available; the host creates its own PostGIS database, never `luxmap_dev`:

```bash
dotnet test tests/LuxMap.Api.Tests/LuxMap.Api.Tests.csproj --disable-build-servers -m:1 \
  --filter 'FullyQualifiedName~LuxMap.Api.Tests.AvatarTests'
```

Back up `src/LuxMap.Modules.Identity/Profiles/AvatarService.cs` with `cp`. Remove **only line 53**:

```csharp
await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM app_user WHERE user_id = {user.UserId} FOR UPDATE", ct);
```

Leave the transaction, re-read, store, SaveChanges, and commit intact. Then run (rebuild; do not use `--no-build`):

```bash
dotnet test tests/LuxMap.Api.Tests/LuxMap.Api.Tests.csproj --disable-build-servers -m:1 \
  --filter 'FullyQualifiedName~LuxMap.Api.Tests.AvatarTests.Upload_holds_account_lock_while_writing_objects'
```

Expected RED: the probe now obtains its lock instead of throwing 55P03; the hook assertion fails, the upload returns 500,
and the test's expected-200 assertion fails. This is deterministic because no competing upload or SaveChanges has run yet.
Restore using `cp` (not `mv`, so MSBuild sees the file change), rebuild and rerun all AvatarTests for GREEN.
Also repeat `Both_concurrent_uploads_succeed_and_leave_one_complete_set` at least 20 times, checking every exit code.
These commands/expected results are instructions, **not claimed execution evidence**.

## Actual verification and sandbox blockers

`git cherry-pick d3347ed` failed before changing files:

```text
error: Unable to create '/Users/nhm809/Documents/LuxMap/luxmap_backend/.git/worktrees/luxmap_backend-avatar-race/index.lock': Operation not permitted
fatal: cherry-pick failed
```

That shared worktree metadata is outside the writable sandbox. No cherry-pick is in progress and no commits were created.
Applied the source patch with `git apply` to source/test/tracking; manually merged the media-storage paragraph because
dev appended ASSET-PHOTOS and WO-POLE-GPS sections. Both those sections and dev's other dashboard entries remain intact.
Corrected the imported dashboard's historical "10/10 green" claim so it does not imply this session ran integration tests.
An explicit-path `git add -- src/LuxMap.Modules.Identity/Profiles/AvatarService.cs tests/LuxMap.Api.Tests/AvatarTests.cs docs/agents/traps/media-storage.md tracking.html`
also failed with the same index.lock permission error. Staging/committing is blocked, not awaiting user authorization.
All Git commands were invoked from this worktree; none pushed or opened a PR.

`dotnet --version`: `10.0.302`.
Requested `dotnet build --disable-build-servers` produced no output and did not finish during observation in this sandbox.
It was still running at handoff (PID `88237`, identified with `lsof -t /tmp/luxmap-avatar-race-build.log`);
an attempted `kill -TERM 88237` was rejected with `operation not permitted`. Claude should stop that stalled process
before rebuilding if it is still present.
Fallback `dotnet build --disable-build-servers -m:1` completed with exit code 0:

```text
Build succeeded.
    46 Warning(s)
    0 Error(s)
Time Elapsed 00:00:33.80
```

Every warning is `NU1900`: unable to load `https://api.nuget.org/v3/index.json` for package vulnerability data.
There are no compiler/analyzer warnings in that log. Audit was not disabled. Thus compilation is verified, but the
requested warning-free network-enabled build is **not** verified. Full log: `/tmp/luxmap-avatar-race-build-single.log`.
`git diff --check` exited 0. Reviewed the final source/test/docs diff and preserved the AGENTS.md symlink.
Integration tests and the break/restore cycle were **not run**, per the no-Docker handoff. Claude must run them.

Once Git metadata is writable, complete the requested cherry-pick/follow-up commit workflow, preserving these working-tree
changes first. Intended Conventional Commit messages (no Co-Authored-By trailer):
`fix(identity): serialize concurrent avatar uploads of one account` (source commit) and
`fix(identity): reread locked avatar account and verify serialization` (follow-up plus this handoff).
Stage only explicit paths, including `.ai/results/AVATAR-RACE.md` and `tracking.html`. Do not push or open a PR.

## Verification run by Claude (13/11/2026, Docker available)

- Commits: `d3347ed` cherry-picked as-is (author kept; media-storage.md conflict resolved keeping dev's ASSET-PHOTOS / WO-POLE-GPS
  sections), then this follow-up. The stalled build PID 88237 was already gone.
- Before the fix (dev `5f1ae62`): `AvatarTests` failed 1 run in 5 on `Both_concurrent_uploads_succeed_and_leave_one_complete_set`.
- After: `AvatarTests` 20 runs, 20 passed.
- Break test: deleting only the `SELECT 1 FROM app_user … FOR UPDATE` line → `Upload_holds_account_lock_while_writing_objects` failed;
  file restored with `cp` + `touch`.
- `dotnet restore --force` with network: 0 NU1900; build 0 warnings. Full `dotnet test`: exit 0 — Api 1686 passed / 7 skipped,
  Shared 424, Persistence 45, Infrastructure.Storage 83.

