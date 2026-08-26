// Package services is a catalogue of common services with their domains.
//
// Whoever wants "TikTok off the children's tablet after 9pm" should not have
// to research which domains that involves first. The catalogue is
// deliberately a curated selection and makes no claim to completeness — what
// is missing belongs in the configuration as an ordinary rule.
package services

import "sort"

// Service is one entry in the catalogue.
type Service struct {
	Key     string   `json:"key"`
	Name    string   `json:"name"`
	Domains []string `json:"domains"`
}

// catalog maps keys to display name and domains.
var catalog = map[string]Service{
	"tiktok": {Key: "tiktok", Name: "TikTok", Domains: []string{
		"tiktok.com", "tiktokcdn.com", "tiktokv.com", "byteoversea.com", "musical.ly",
	}},
	"youtube": {Key: "youtube", Name: "YouTube", Domains: []string{
		"youtube.com", "youtu.be", "ytimg.com", "googlevideo.com", "youtubei.googleapis.com",
	}},
	"instagram": {Key: "instagram", Name: "Instagram", Domains: []string{
		"instagram.com", "cdninstagram.com", "ig.me",
	}},
	"facebook": {Key: "facebook", Name: "Facebook", Domains: []string{
		"facebook.com", "fbcdn.net", "fb.com", "fbsbx.com",
	}},
	"whatsapp": {Key: "whatsapp", Name: "WhatsApp", Domains: []string{
		"whatsapp.com", "whatsapp.net", "wa.me",
	}},
	"snapchat": {Key: "snapchat", Name: "Snapchat", Domains: []string{
		"snapchat.com", "sc-cdn.net", "snap.com",
	}},
	"x": {Key: "x", Name: "X (Twitter)", Domains: []string{
		"twitter.com", "x.com", "twimg.com", "t.co",
	}},
	"reddit": {Key: "reddit", Name: "Reddit", Domains: []string{
		"reddit.com", "redd.it", "redditmedia.com", "redditstatic.com",
	}},
	"twitch": {Key: "twitch", Name: "Twitch", Domains: []string{
		"twitch.tv", "ttvnw.net", "jtvnw.net",
	}},
	"discord": {Key: "discord", Name: "Discord", Domains: []string{
		"discord.com", "discord.gg", "discordapp.com", "discordapp.net",
	}},
	"telegram": {Key: "telegram", Name: "Telegram", Domains: []string{
		"telegram.org", "t.me", "telegram.me", "telesco.pe",
	}},
	"netflix": {Key: "netflix", Name: "Netflix", Domains: []string{
		"netflix.com", "nflxvideo.net", "nflximg.net", "nflxext.com",
	}},
	"disneyplus": {Key: "disneyplus", Name: "Disney+", Domains: []string{
		"disneyplus.com", "disney-plus.net", "dssott.com",
	}},
	"primevideo": {Key: "primevideo", Name: "Prime Video", Domains: []string{
		"primevideo.com", "aiv-cdn.net", "aiv-delivery.net",
	}},
	"spotify": {Key: "spotify", Name: "Spotify", Domains: []string{
		"spotify.com", "scdn.co", "spotifycdn.com",
	}},
	"steam": {Key: "steam", Name: "Steam", Domains: []string{
		"steampowered.com", "steamcommunity.com", "steamstatic.com", "steamcontent.com",
	}},
	"epicgames": {Key: "epicgames", Name: "Epic Games", Domains: []string{
		"epicgames.com", "unrealengine.com", "fortnite.com",
	}},
	"roblox": {Key: "roblox", Name: "Roblox", Domains: []string{
		"roblox.com", "rbxcdn.com", "roblox.co",
	}},
	"minecraft": {Key: "minecraft", Name: "Minecraft", Domains: []string{
		"minecraft.net", "minecraftservices.com", "mojang.com",
	}},
	"playstation": {Key: "playstation", Name: "PlayStation", Domains: []string{
		"playstation.com", "playstation.net", "sonyentertainmentnetwork.com",
	}},
	"xbox": {Key: "xbox", Name: "Xbox", Domains: []string{
		"xbox.com", "xboxlive.com", "xboxservices.com",
	}},
	"nintendo": {Key: "nintendo", Name: "Nintendo", Domains: []string{
		"nintendo.net", "nintendo.com", "nintendoswitch.com",
	}},
	"pinterest": {Key: "pinterest", Name: "Pinterest", Domains: []string{
		"pinterest.com", "pinimg.com", "pin.it",
	}},
	"linkedin": {Key: "linkedin", Name: "LinkedIn", Domains: []string{
		"linkedin.com", "licdn.com", "lnkd.in",
	}},
	"imgur": {Key: "imgur", Name: "Imgur", Domains: []string{"imgur.com", "imgur.io"}},
	"9gag":  {Key: "9gag", Name: "9GAG", Domains: []string{"9gag.com", "9cache.com"}},
	"vimeo": {Key: "vimeo", Name: "Vimeo", Domains: []string{"vimeo.com", "vimeocdn.com"}},
	"soundcloud": {Key: "soundcloud", Name: "SoundCloud", Domains: []string{
		"soundcloud.com", "sndcdn.com",
	}},
	"openai": {Key: "openai", Name: "OpenAI / ChatGPT", Domains: []string{
		"openai.com", "chatgpt.com", "oaistatic.com", "oaiusercontent.com",
	}},
	"tinder": {Key: "tinder", Name: "Tinder", Domains: []string{"tinder.com", "gotinder.com"}},
	"onlyfans": {Key: "onlyfans", Name: "OnlyFans", Domains: []string{
		"onlyfans.com", "onlyfans.net", "ofcdn.net",
	}},
	"zoom": {Key: "zoom", Name: "Zoom", Domains: []string{"zoom.us", "zoomgov.com"}},

	// Public DoH providers. Blocking them forces a browser with encrypted
	// resolution of its own back onto the network's resolver — otherwise it
	// walks past the filter without anyone noticing.
	//
	// Your own upstream is unaffected: Auspex resolves its host name through
	// the bootstrap resolver, not through its own filter.
	"doh-anbieter": {Key: "doh-anbieter", Name: "Public DoH providers", Domains: []string{
		"cloudflare-dns.com",
		"mozilla.cloudflare-dns.com",
		"chrome.cloudflare-dns.com",
		"security.cloudflare-dns.com",
		"family.cloudflare-dns.com",
		"dns.google",
		"dns64.dns.google",
		"dns.quad9.net",
		"dns10.quad9.net",
		"dns11.quad9.net",
		"doh.opendns.com",
		"doh.familyshield.opendns.com",
		"dns.nextdns.io",
		"doh.cleanbrowsing.org",
		"dns.adguard-dns.com",
		"dns.adguard.com",
		"doh.mullvad.net",
		"dns.controld.com",
		"doh.dns.sb",
		"dns.digitale-gesellschaft.ch",
		"doh-de.blahdns.com",
		"dnsforge.de",
		"doh.libredns.gr",
	}},
}

// Lookup returns one service entry.
func Lookup(key string) (Service, bool) {
	s, ok := catalog[key]
	return s, ok
}

// All returns the catalogue, sorted by display name.
func All() []Service {
	out := make([]Service, 0, len(catalog))
	for _, s := range catalog {
		out = append(out, s)
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Name < out[j].Name })
	return out
}

// Rules translates service keys into block rules. Unknown keys come back as
// a second return value rather than being silently dropped — a typo should
// be noticed and not end up as a silently permitted service.
func Rules(keys []string) ([]string, []string) {
	var rules, unknown []string
	for _, key := range keys {
		service, ok := Lookup(key)
		if !ok {
			unknown = append(unknown, key)
			continue
		}
		for _, domain := range service.Domains {
			rules = append(rules, "||"+domain+"^")
		}
	}
	return rules, unknown
}
