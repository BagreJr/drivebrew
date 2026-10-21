# Homebrewery real-document test corpus

These files are byte-for-byte downloads from the Google Drive folder supplied by the project owner. They are kept outside `sources/` and are used only as the Phase 2 local catalog and renderer test corpus.

| File | Google Drive file ID | Modified in Drive | SHA-256 |
|---|---|---:|---|
| `Magus.txt` | `1Lt24mPs0pGIDqmm5aLQ_XlgPOFghYsPV` | 2026-10-08 | `9292467d8b80ee4639d72ce374dbf499ae4e239cd22485123f6775b8ed5e8d05` |
| `Psion.txt` | `1Zc4cwAUVU28oWiUAErfVQk_q833n5q0X` | 2026-10-12 | `7fc6a2b6929861ddb6f0a68847e748506bb42d7ac515f8d7c9e3da640761deb0` |
| `Backgrounds, Feats & Equipment.txt` | `1he3Fc00DQZVKi58eB6vj_AKhe2mK6SYt` | 2026-08-05 | `1aaafcf500c7ea8c4abc215217be44cff718e4bbd1b536fef2d0f1823c079d5f` |

The app does not contact Drive to open these copies. Runtime Google Drive synchronization remains outside Phase 2.

The three files under `resources/covers/` are the HTTPS images referenced by page 1 of each TXT. They are retained only to make the offline visual regression deterministic:

| Local file | Original URL | SHA-256 |
|---|---|---|
| `magus-cover.jpg` | `https://i.imgur.com/ov0LfAA.jpg` | `3e2b76620c7ea17784c4d1be2c15d507fca3d7f2ab803d792bf51ad6e7c43f09` |
| `psion-cover.png` | `https://i.imgur.com/6gQDRuK.png` | `97b34a639139c2f38249de20582ed9373993fd74fd242e3df66e315c05f8defa` |
| `backgrounds-cover.jpg` | `https://i.imgur.com/IOyNUC1.jpg` | `433aeb52ba55c5295a7aaece512903c887492cbe9be9587fe5127269b70c3bfa` |
