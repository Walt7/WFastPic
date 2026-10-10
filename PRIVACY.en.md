# Privacy

[Italiano](PRIVACY.md)

WFastPic works on your PC. It has no accounts, ads, usage statistics or telemetry: nobody knows who uses
it or how. **Photos, faces, people's names and file names never leave your PC**: people recognition and
automatic rating also run entirely locally.

## What stays on your PC

- **Your original photos**: never modified, never sent.
- **Next to your photos**, in the hidden `.wfastpic` folder of every opened folder: selections, stars,
  edits, thumbnails, faces found (box, small thumbnail and numeric fingerprint), automatic rating
  measurements, place names already looked up.
- **In `%LocalAppData%\WFastPic`**: settings, named people (name, picture, face fingerprints), people
  recognition models and error reports.

## What leaves your PC, and when

Only these requests, each tied to a feature you are using:

| Feature | To | What is sent | When | How to avoid it |
|---|---|---|---|---|
| Place names ("Polignano a Mare, Puglia") | `nominatim.openstreetmap.org` | the photo's GPS coordinates and the language | when you show the place of a photo with GPS; once per ~100 m area, then it is saved | map → untick "Place names (OpenStreetMap)" |
| Map and mini-map | `tile.openstreetmap.org`, `server.arcgisonline.com` (Esri) | which map areas you are looking at | only with the map open | do not open the map |
| Map library (Leaflet) | `unpkg.com` | nothing of yours | when the map opens | do not open the map |
| People models (38 MB) | `huggingface.co` | nothing of yours | once, if you turn on people recognition | do not turn it on |
| New version notice | `api.github.com` | nothing of yours | at every start | About → untick "Tell me when a new version is available" |
| "Report the problem" | `github.com` | the error report, already cleaned of paths, file names, user name and PC name | only if you press the button **and** send the report on GitHub yourself | do not press it |

As with any internet request, these services see your PC's IP address. Their policies:
[OpenStreetMap Foundation](https://osmfoundation.org/wiki/Privacy_Policy),
[Esri](https://www.esri.com/en-us/privacy/overview),
[Hugging Face](https://huggingface.co/privacy),
[GitHub](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement).

## Deleting your data

Uninstalling WFastPic keeps your data, on purpose (selections and people are not lost with an update).
To remove everything: delete the `%LocalAppData%\WFastPic` folder and the hidden `.wfastpic` folders next
to your photos. Your photos are not touched.

## Changes

If a new version changes anything described here, you will find it on this page and in the
[CHANGELOG](CHANGELOG.en.md).
