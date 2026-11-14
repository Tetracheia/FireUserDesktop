# Account language

English, Arabic and Greek account language support is coordinated with the backend's docs/account-language.md, docs/requirements/ and both provider migrations. The backend document is the cross-client contract and verification/release record.

This client uses explicit developer-authored text catalogs and its existing account preference/profile flow. Arabic uses right-to-left interface layout. Chat text, names, resource titles and technical identifiers stay literal. Server preference loads on account initialization and reconnect/owner invalidation; no polling is added. Before identifying an account, English is the default. Unknown catalog keys fall back to English.

Catalogs are generated from the Angular public/locales source using its scripts/sync-locales.mjs. Keep generated copies aligned; each checkout builds independently. Physical-device/native-speaker validation and actual deployment status must be read from the backend verification record, not inferred from catalog presence.

Documentation assessment: FR, NFR, conceptual schema, sequence, formal use case and traceability are updated in the backend contract; Angular additionally updates its existing docs/requirements artifacts. This client's existing account, media, storage and permission domain contracts were reviewed unchanged.


### Local desktop launch check (2026-11-14)

The installed Linux application and autostart entries pointed to the older
`bin/Debug/net10.0` build. Both the old build and current Release build passed
`--startup-smoke-test` in the current graphical session; a startup crash was
not reproduced. Starting the current Release build normally opened its native
window and refreshed both entries through the existing registration code to
`bin/Release/net10.0`. Application-menu activation was also exercised. No
account settings or credentials were changed, and no source fix was required.

Documentation impact: functional requirements, non-functional requirements,
conceptual schema, sequences, use cases and traceability were reviewed unchanged;
this was a local launcher repair. Full signed-in workflows and startup after a
fresh login/reboot were not checked.
