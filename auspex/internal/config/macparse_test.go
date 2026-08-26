package config

import "testing"

// A profile that fails to apply because of spelling is the worst kind of
// fault: nothing breaks, nothing is reported, the child's device is just
// suddenly browsing unfiltered. So every common spelling is brought to the
// same form.

func TestMacSpellingsLandTheSame(t *testing.T) {
	erwartet := "00:00:5e:00:53:0e"

	for _, roh := range []string{
		"00:00:5e:00:53:0e",
		"00:00:5E:00:53:0E",
		"00-00-5e-00-53-0e",
		"00-00-5E-00-53-0E",
		"  00:00:5E:00:53:0E  ",
		"mac:00:00:5E:00:53:0E",
	} {
		got, err := ParseMac(roh)
		if err != nil {
			t.Errorf("%q: unexpected error %v", roh, err)
			continue
		}
		if got != erwartet {
			t.Errorf("%q: expected %s, got %s", roh, erwartet, got)
		}
	}
}

func TestUnusableMacsAreRejected(t *testing.T) {
	// Better an error at startup than a profile that never applies.
	for _, roh := range []string{
		"",
		"no-mac",
		"00:00:5e:00:53",       // too short
		"00:00:5e:00:53:0e:ff", // too long for Ethernet
		"gg:56:0f:19:1e:0e",    // not hex digits
		"192.168.1.43",
	} {
		if _, err := ParseMac(roh); err == nil {
			t.Errorf("%q should have been rejected", roh)
		}
	}
}

func TestAnEightByteMacIsNotAccepted(t *testing.T) {
	// net.ParseMAC also accepts EUI-64. Nothing like that turns up on an
	// Ethernet frame in a home network, and silently storing something other
	// than what the user meant would be worse than a refusal.
	if got, err := ParseMac("00:00:5e:00:53:0e:aa:bb"); err == nil {
		t.Errorf("EUI-64 should not pass as a device MAC, got %q", got)
	}
}
