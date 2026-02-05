# Timeline Editor – Quick Start

**Timeline Editor** is a desktop tool for visualizing and editing musical timelines. It works alongside the `notechart` Python package to generate and display notecharts from audio.

## Features

- Load audio files and view pitch graphs
- Edit, move, or delete notes interactively
- Generate timelines with [`notechart`](#notechart) and visualize them
- Export data for Unity or other projects

## Requirements

- Windows OS
- .NET 6.0 or later
- Python with [`notechart`](#notechart) installed

```bash
pip install notechart
```

- NAudio and NWaves (included via NuGet)

## Installation
No install required, simply download the latest release binary and run it.
Download latest release from the [GitHub Releases](https://github.com/GeekStories/Notechart-TimelineEditor/releases)

## Development

Clone the repository:

```bash
git clone https://github.com/yourusername/timeline-editor.git
cd timeline-editor
```

Build the project:

```bash
dotnet build
```

Run the editor:

```bash
dotnet run
```

## Usage

1. Load an audio file (WAV recommended) or existing timeline data file.
*Note: Audio file must be .WAV, Mono, Vocal Track (Only Vocals, no other sound) for more accuerate results*
2. Adjust Generation settings as needed.
3. The pitch and notes are generated automatically.
3. Scroll and edit notes on the timeline.
5. Export timeline data for use in projects, or OpenStar.


# notechart

[notechart](https://github.com/GeekStories/notechart) is a **separate repository** responsible for audio analysis and automatic note generation.

The workflow looks like this:

1. Audio is loaded into the Timeline Editor.
2. The editor invokes `notechart` as a command-line tool.
3. `notechart` analyzes the audio and outputs timeline data.
4. Timeline Editor parses this data and displays it visually.
5. Manual edits can be made and saved in a `notechart`-compatible format.

This hybrid approach combines automated chart generation with precise manual control.

## License

MIT License – see `LICENSE` for details.
