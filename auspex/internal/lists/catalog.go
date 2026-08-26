package lists

// Known is a curated selection of proven filter lists. Whoever wants to add
// a list should not have to go hunting for URLs first.
//
// Deliberately kept short: stacking several large lists buys hardly any more
// blocking but considerably more false positives. One is usually enough.
//
// Description is German and stays that way. The resolver answers an API
// request, not a person with a language setting — it cannot know which
// language somebody is reading in. The dashboard therefore replaces the text
// when displaying it, via Strings.ListDescription, looked up by name. What
// stands here is the fallback for anything querying the API directly.
type Known struct {
	Name        string `json:"name"`
	URL         string `json:"url"`
	Description string `json:"description"`
	Allow       bool   `json:"allow"`
}

var known = []Known{
	{
		Name:        "hagezi-multi-pro",
		URL:         "https://raw.githubusercontent.com/hagezi/dns-blocklists/main/adblock/pro.txt",
		Description: "Werbung und Tracking, ausgewogen. Gute Standardwahl für den Alltag.",
	},
	{
		Name:        "hagezi-multi-pro-plus",
		URL:         "https://raw.githubusercontent.com/hagezi/dns-blocklists/main/adblock/pro.plus.txt",
		Description: "Deutlich strenger als Pro. Rechne mit gelegentlichen Fehlalarmen.",
	},
	{
		Name:        "oisd-big",
		URL:         "https://big.oisd.nl/",
		Description: "Umfangreiche Allzweckliste, gepflegt auf wenige Fehlalarme.",
	},
	{
		Name:        "hagezi-threat-intelligence",
		URL:         "https://raw.githubusercontent.com/hagezi/dns-blocklists/main/adblock/tif.txt",
		Description: "Schadsoftware, Phishing, Betrug. Ergänzt eine Werbeliste, ersetzt sie nicht.",
	},
	{
		Name:        "stevenblack-hosts",
		URL:         "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts",
		Description: "Klassische Hosts-Datei, konservativ und weit verbreitet.",
	},
	{
		Name:        "hagezi-fake",
		URL:         "https://raw.githubusercontent.com/hagezi/dns-blocklists/main/adblock/fake.txt",
		Description: "Gefälschte Shops und Betrugsseiten.",
	},
}

// KnownLists returns the catalogue.
func KnownLists() []Known { return known }
