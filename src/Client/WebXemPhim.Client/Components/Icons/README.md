# CinemaFlow icon library

`CinemaIcon` is the single icon entry point for the Blazor client. The bundled
glyphs are a curated, locally hosted subset of the MIT-licensed Tabler Icons
family, normalized to a 24px view box and a default stroke width of 1.75.

```razor
<CinemaIcon Name="CinemaIconName.Play" />
<CinemaIcon Name="CinemaIconName.Search" Size="18" />
<CinemaIcon Name="CinemaIconName.User" Title="Tài khoản" />
```

Icons are decorative by default and render with `aria-hidden="true"`. Pass a
`Title` only when the icon carries meaning without adjacent visible text.

Do not add inline SVG to pages. Add a member to `CinemaIconName`, then add the
matching Tabler glyph to `CinemaIcon.razor` so stroke, sizing, and accessibility
stay consistent.

Source: Tabler Icons, https://tabler.io/icons, MIT License.
