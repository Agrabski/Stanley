# Panel layout dragging

How panel edges and gaps are grabbed on the page canvas (`PageCanvasControl.HitTest`).

- **Gutter drag (default).** `PanelGutters.FindAt` finds the gap between neighbouring panels
  (plus a tolerance band that overlaps the panels' own edge bands) and the whole run of
  panels lining up along it. Dragging moves that boundary as one, keeping the gap width
  (`PanelLayoutEditing.DragBoundary`). Gutters win over a panel's plain edge band, so a
  panel edge next to a neighbour can't be grabbed on its own with a bare press.
- **Single edge (Ctrl/Cmd + drag on a gutter).** `SingleEdgeOfGutter` swaps the gutter hit for
  a `PanelEdge` hit on the one panel edge nearest the pointer (the panel on the pointer's side
  that spans the pointer across the gap), so the drag is an ordinary panel resize: the
  neighbour and the gap's other side stay put, letting the gap shrink or grow (issue #88).
- Locked layouts offer neither; thought clouds never form gutters.
