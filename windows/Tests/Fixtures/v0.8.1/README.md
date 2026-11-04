# v0.8.1 upgrade fixtures

These synthetic files were serialized using the unmodified Core project from release v0.8.1,
commit 47959de3a9bd95c0d79155b4a8ada9e0d06606c7, through Secrets.SerializeSealed and Backup.Export.
They include paused HTTP/HLS progress, finished site video, queues, settings and fake encrypted credentials.
There are no real credentials or user data. The test-only protector encodes UTF-8 `fixture:` plus the value
as Base64; the original serializer adds its normal `dpapi:` field prefix. This exercises the saved-data
contract across versions without pretending to test another user's DPAPI or Linux keyring credentials.

UpgradeTests uses the current production readers to check history, progress, engine selection, credentials,
opt-in defaults and backup safety. Platform CI also exercises an actual Windows installer upgrade separately.
