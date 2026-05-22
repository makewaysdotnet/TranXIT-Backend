# TranXIT Backend GitFlow

## Branches
- `main`: production-ready release branch.
- `Development`: integration branch for reviewed work.
- `feature/<name>`: active implementation branches created from `Development`.
- `hotfix/<name>`: urgent fixes created from `main`, then merged back into `main` and `Development`.

## Working Flow
1. Fetch latest remote branches before starting work.
2. Create feature branches from `origin/Development`.
3. Keep commits small and scoped by intent.
4. Run `dotnet restore` and `dotnet build TranXit.sln --no-restore` before pushing.
5. Push feature branches to origin and open review back into `Development`.
6. Promote `Development` to `main` only for release-ready builds.

Current MVP branch: `feature/web-first-mvp`.
