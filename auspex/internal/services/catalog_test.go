package services

import "testing"

func TestRulesTranslatesServices(t *testing.T) {
	rules, unknown := Rules([]string{"tiktok"})

	if len(unknown) != 0 {
		t.Fatalf("unknown = %v", unknown)
	}
	if len(rules) == 0 {
		t.Fatal("no rules produced")
	}
	// AdBlock syntax, so the existing parser reads them with no special case.
	if rules[0] != "||tiktok.com^" {
		t.Errorf("first rule = %q, expected ||tiktok.com^", rules[0])
	}
}

// A typo must not end up as a silently permitted service.
func TestAnUnknownServiceIsReported(t *testing.T) {
	rules, unknown := Rules([]string{"youtube", "tikttok"})

	if len(unknown) != 1 || unknown[0] != "tikttok" {
		t.Fatalf("unknown = %v, expected [tikttok]", unknown)
	}
	if len(rules) == 0 {
		t.Error("the valid services should still be translated")
	}
}

func TestTheCatalogueIsSortedAndComplete(t *testing.T) {
	all := All()
	if len(all) < 20 {
		t.Fatalf("only %d services in the catalogue", len(all))
	}
	for i := 1; i < len(all); i++ {
		if all[i-1].Name > all[i].Name {
			t.Fatalf("not sorted: %q before %q", all[i-1].Name, all[i].Name)
		}
	}
	for _, s := range all {
		if s.Key == "" || s.Name == "" || len(s.Domains) == 0 {
			t.Errorf("incomplete entry: %+v", s)
		}
	}
}
