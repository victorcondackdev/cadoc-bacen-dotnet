module github.com/victorcondackdev/cadoc-bacen-dotnet/tools/vecgen

go 1.22

// The generator runs against a local checkout of the Go implementation,
// cloned next to this repository (../cadoc-bacen-go relative to the repository root).
replace github.com/victorcondackdev/cadoc-bacen-go => ../../../cadoc-bacen-go

require github.com/victorcondackdev/cadoc-bacen-go v0.0.0-00010101000000-000000000000
