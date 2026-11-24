// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/umgah/umgahc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Umgah (umgahc.c), whole: in space and at their homeworld
/// (GLOBAL_FLAGS_AND_DATA bit 7). Hostile from init unless the Talking Pet
/// has been taken from them and they aren't hostile for good
/// (TALKING_PET without UMGAH_HOSTILE), so the encounter screen comes
/// first. Before the Pet (the only Umgah play reaches before stage 3c):
/// their greetings by place and visit, two questions, and every way out a
/// fight ("can we be friends?" once: UMGAH_MENTIONED_TRICKS; "evil
/// blobbies, give up" once: UMGAH_EVIL_BLOBBIES); a talk with them not
/// yet zombies marks them met (MET_NORMAL_UMGAH, post_umgah_enc: an
/// attack from the encounter screen too). The Pet's zombies
/// (UMGAH_ZOMBIE_BLOBBIES, set by stage 3c's Arilou): strangers greeted
/// and questioned (the captain's and the ship's names spliced into "we
/// are"; by MET_NORMAL_UMGAH what they said before is thrown at them),
/// leaving at peace or, the secret told (KNOW_UMGAH_ZOMBIES, their visits
/// counted afresh), a fight; once it's known, or the Pet has been met
/// (TALKING_PET_VISITS), the greeting alone and a fight. With the Pet
/// taken (TALKING_PET): in space a greeting sending the players home, at
/// peace; at their homeworld the reward: the answering ship's bio data
/// by 500 (1000 / BIO_CREDIT_VALUE: one of two replies, the other gone
/// with it), their questions, and at the only goodbye 4 ships to the
/// host's fleet by its room (AddEscortShips: the plan's question 4) and
/// UMGAH_HOSTILE: the talk then ends in a fight against their endless
/// fleet (CombatIsInevitable sets the segue hostile for every reply but
/// the zombies' two peaceful goodbyes), and they greet as enemies from
/// then on. Every path is checked against UQM's own script
/// (tests/golden/talks-umgah.json).
/// <para>
/// UQM's talk can't be left before its end; the port's can (its player
/// leaves, or the host saves). The bio data is given at the reply and the
/// reward is closed only by the goodbye (UMGAH_HOSTILE), so a reward
/// talk dropped after the bio data was given closes it all the same
/// (<see cref="DroppedUnfinished"/>: UMGAH_HOSTILE set, the ships, the
/// goodbye's, not given), or it could be had again.
/// </para>
/// </summary>
public sealed class UmgahScript : CommScript
{
    // races.h's UMGAH_SHIP.
    private const int UmgahShip = 17;

    // sis.h's BIO_CREDIT_VALUE.
    private const int BioCreditValue = 2;

    // The port's own: the reward's bio data given in this talk, its goodbye not yet said.
    private bool _rewarded;

    public UmgahScript(Data.ConversationScript phrases) : base(phrases)
    {
    }

    private bool AtHome => (GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 7)) != 0;

    private void CombatIsInevitable(int r)
    {
        T.SetSegue(Segue.Hostile);

        if (PlayerSaid(r, "bye_zombie"))
        {
            NPCPhrase("GOODBYE_ZOMBIE");

            T.SetSegue(Segue.Peace);
        }
        else if (PlayerSaid(r, "bye_pre_zombie"))
            NPCPhrase("GOODBYE_PRE_ZOMBIE");
        else if (PlayerSaid(r, "can_we_be_friends"))
        {
            NPCPhrase("SURE_FRIENDS");

            SetGameState("UMGAH_MENTIONED_TRICKS", 1);
        }
        else if (PlayerSaid(r, "evil_blobbies_give_up"))
        {
            NPCPhrase("NOT_EVIL_BLOBBIES");

            SetGameState("UMGAH_EVIL_BLOBBIES", 1);
        }
        else if (PlayerSaid(r, "evil_blobbies_must_die"))
            NPCPhrase("OH_NO_WE_WONT");
        else if (PlayerSaid(r, "threat"))
            NPCPhrase("NO_THREAT");
        else if (PlayerSaid(r, "dont_believe"))
        {
            NPCPhrase("THEN_DIE");

            SetGameState("KNOW_UMGAH_ZOMBIES", 1);
            SetGameState("UMGAH_VISITS", 0);
        }
        else if (PlayerSaid(r, "bye_unknown"))
        {
            NPCPhrase("GOODBYE_UNKNOWN");

            T.SetSegue(Segue.Peace);
        }
        else if (PlayerSaid(r, "bye_post_zombie"))
        {
            NPCPhrase("FUNNY_IDEA");

            T.AlienTalkSegue(Talk.WaitAll);
            // To the host's fleet, whoever answers, by its room.
            T.AddEscortShips(UmgahShip, 4);
            SetGameState("UMGAH_HOSTILE", 1);
            _rewarded = false;
        }
    }

    private void Zombies(int r)
    {
        if (GetGameState("MET_NORMAL_UMGAH") != 0)
        {
            if (PlayerSaid(r, "whats_up_zombies"))
            {
                NPCPhrase("GENERAL_INFO_ZOMBIE");

                DisablePhrase("whats_up_zombies");
            }
            else if (PlayerSaid(r, "how_goes_tpet"))
            {
                NPCPhrase("WHAT_TPET");

                DisablePhrase("how_goes_tpet");
            }
            else if (PlayerSaid(r, "you_told_us"))
            {
                NPCPhrase("SADLY_IT_DIED");

                DisablePhrase("you_told_us");
            }

            if (PhraseEnabled("whats_up_zombies") && PhraseEnabled("how_goes_tpet"))
                Response("whats_up_zombies", Zombies);
            if (PhraseEnabled("how_goes_tpet"))
                Response("how_goes_tpet", Zombies);
            else if (PhraseEnabled("you_told_us"))
                Response("you_told_us", Zombies);
            else
                Response("dont_believe", CombatIsInevitable);
            if (PhraseEnabled("whats_up_zombies") && !PhraseEnabled("how_goes_tpet"))
                Response("whats_up_zombies", Zombies);
            Response("threat", CombatIsInevitable);
            Response("bye_unknown", CombatIsInevitable);
        }
        else
        {
            int lastStack = 0;
            var pStr = new string?[4];
            if (PlayerSaid(r, "evil_blobbies"))
            {
                NPCPhrase("YES_VERY_EVIL");

                DisablePhrase("evil_blobbies");
                lastStack = 0;
            }
            else if (PlayerSaid(r, "we_vindicator0"))
            {
                NPCPhrase("GOOD_FOR_YOU_1");

                DisablePhrase("we_vindicator0");
                lastStack = 1;
            }
            else if (PlayerSaid(r, "come_in_peace"))
            {
                NPCPhrase("GOOD_FOR_YOU_2");

                DisablePhrase("come_in_peace");
                lastStack = 1;
            }
            else if (PlayerSaid(r, "know_any_jokes"))
            {
                NPCPhrase("JOKE_1");

                DisablePhrase("know_any_jokes");
                lastStack = 2;
            }
            else if (PlayerSaid(r, "better_joke"))
            {
                NPCPhrase("JOKE_2");

                DisablePhrase("better_joke");
                lastStack = 2;
            }
            else if (PlayerSaid(r, "not_very_funny"))
            {
                NPCPhrase("YES_WE_ARE");

                DisablePhrase("not_very_funny");
                lastStack = 2;
            }
            else if (PlayerSaid(r, "what_about_tpet"))
            {
                NPCPhrase("WHAT_TPET");

                DisablePhrase("what_about_tpet");
                lastStack = 3;
            }
            else if (PlayerSaid(r, "give_up_or_die"))
            {
                NPCPhrase("NOT_GIVE_UP");

                T.SetSegue(Segue.Hostile);
                return;
            }
            else if (PlayerSaid(r, "arilou_told_us"))
            {
                NPCPhrase("THEN_DIE");

                T.SetSegue(Segue.Hostile);
                SetGameState("KNOW_UMGAH_ZOMBIES", 1);
                SetGameState("UMGAH_VISITS", 0);
                return;
            }

            if (PhraseEnabled("evil_blobbies"))
                pStr[0] = "evil_blobbies";
            else
                pStr[0] = "give_up_or_die";

            if (PhraseEnabled("we_vindicator0"))
            {
                T.ConstructResponse("we_vindicator0", Sis.CaptainName, "we_vindicator1", Sis.ShipName, "we_vindicator2");
                pStr[1] = "we_vindicator0";
            }
            else if (PhraseEnabled("come_in_peace"))
                pStr[1] = "come_in_peace";

            if (PhraseEnabled("know_any_jokes"))
                pStr[2] = "know_any_jokes";
            else if (PhraseEnabled("better_joke"))
                pStr[2] = "better_joke";
            else if (PhraseEnabled("not_very_funny"))
                pStr[2] = "not_very_funny";

            if (PhraseEnabled("what_about_tpet"))
                pStr[3] = "what_about_tpet";
            else
                pStr[3] = "arilou_told_us";

            if (pStr[lastStack] is { } last)
                Offer(last);
            for (int i = 0; i < 4; ++i)
            {
                if (i != lastStack && pStr[i] is { } other)
                    Offer(other);
            }
            Response("bye_zombie", CombatIsInevitable);
        }

        void Offer(string id)
        {
            if (id != "we_vindicator0")
                Response(id, Zombies);
            else
                T.DoResponsePhrase(id, Zombies, Talk.SharedPhraseBuffer);
        }
    }

    private void NormalUmgah(int r)
    {
        if (PlayerSaid(r, "whats_up_pre_zombie"))
        {
            NPCPhrase("GENERAL_INFO_PRE_ZOMBIE");

            DisablePhrase("whats_up_pre_zombie");
        }
        else if (PlayerSaid(r, "want_to_defeat_urquan"))
        {
            NPCPhrase("FINE_BY_US");

            DisablePhrase("want_to_defeat_urquan");
        }

        if (GetGameState("UMGAH_EVIL_BLOBBIES") == 0)
            Response("evil_blobbies_give_up", CombatIsInevitable);
        else
            Response("evil_blobbies_must_die", CombatIsInevitable);
        if (PhraseEnabled("whats_up_pre_zombie"))
            Response("whats_up_pre_zombie", NormalUmgah);
        if (PhraseEnabled("want_to_defeat_urquan"))
            Response("want_to_defeat_urquan", NormalUmgah);
        switch (GetGameState("UMGAH_MENTIONED_TRICKS"))
        {
            case 0:
                Response("can_we_be_friends", CombatIsInevitable);
                break;
        }
        Response("bye_pre_zombie", CombatIsInevitable);
    }

    private void UmgahReward(int r)
    {
        if (PlayerSaid(r, "what_before_tpet"))
        {
            NPCPhrase("TRKD_SPATHI_AND_ILWRATH");

            DisablePhrase("what_before_tpet");
        }
        else if (PlayerSaid(r, "where_caster"))
        {
            NPCPhrase("SPATHI_TOOK_THEM");

            DisablePhrase("where_caster");
        }
        else if (PlayerSaid(r, "owe_me_big_time"))
        {
            NPCPhrase("THANKS");

            // GLOBAL_SIS (TotalBioMass) += 1000 / BIO_CREDIT_VALUE: the answering ship's.
            Sis.BioData += 1000 / BioCreditValue;
            _rewarded = true;
            DisablePhrase("owe_me_big_time");
            DisablePhrase("our_largesse");
        }
        else if (PlayerSaid(r, "our_largesse"))
        {
            NPCPhrase("GIVE_LIFEDATA");

            Sis.BioData += 1000 / BioCreditValue;
            _rewarded = true;
            DisablePhrase("our_largesse");
            DisablePhrase("owe_me_big_time");
        }
        else if (PlayerSaid(r, "what_do_with_tpet"))
        {
            NPCPhrase("TRICK_URQUAN");

            DisablePhrase("what_do_with_tpet");
        }
        else if (PlayerSaid(r, "any_jokes"))
        {
            NPCPhrase("SURE");

            DisablePhrase("any_jokes");
        }
        else if (PlayerSaid(r, "so_what_for_now"))
        {
            NPCPhrase("DO_THIS_NOW");

            DisablePhrase("so_what_for_now");
        }

        if (GetGameState("MET_NORMAL_UMGAH") == 0)
        {
            if (PhraseEnabled("what_before_tpet"))
                Response("what_before_tpet", UmgahReward);
            else if (PhraseEnabled("where_caster"))
                Response("where_caster", UmgahReward);
        }
        if (PhraseEnabled("owe_me_big_time"))
        {
            Response("owe_me_big_time", UmgahReward);
            Response("our_largesse", UmgahReward);
        }
        if (PhraseEnabled("what_do_with_tpet"))
            Response("what_do_with_tpet", UmgahReward);
        else if (PhraseEnabled("any_jokes") && GetGameState("UMGAH_MENTIONED_TRICKS") < 2)
            Response("any_jokes", UmgahReward);
        if (PhraseEnabled("so_what_for_now"))
            Response("so_what_for_now", UmgahReward);
        Response("bye_post_zombie", CombatIsInevitable);
    }

    // The reward's bio data given and its goodbye not said: a talk dropped
    // now closes the reward as the goodbye would (see the class).
    protected internal override bool SettlesWhenDropped => _rewarded;

    protected internal override void DroppedUnfinished(ITalkStory story)
    {
        if (_rewarded)
            story.Set("UMGAH_HOSTILE", 1);
    }

    // The greetings by visit: each switch's last case keeps its count there.
    // The count is written by the caller where umgahc.c writes it later.
    private int Greeting(int numVisits, params string[] hellos)
    {
        if (numVisits < hellos.Length)
            NPCPhrase(hellos[numVisits]);
        if (numVisits == hellos.Length - 1)
            --numVisits;
        return numVisits + 1;
    }

    protected internal override void Intro()
    {
        int numVisits;

        if (GetGameState("UMGAH_HOSTILE") != 0)
        {
            numVisits = Greeting(GetGameState("UMGAH_VISITS"),
                "HOSTILE_HELLO_1", "HOSTILE_HELLO_2", "HOSTILE_HELLO_3", "HOSTILE_HELLO_4");
            SetGameState("UMGAH_VISITS", numVisits);

            T.SetSegue(Segue.Hostile);
        }
        else if (GetGameState("UMGAH_ZOMBIE_BLOBBIES") != 0)
        {
            numVisits = GetGameState("UMGAH_VISITS");
            if (GetGameState("TALKING_PET_VISITS") != 0)
            {
                numVisits = Greeting(numVisits, "DESTROY_INTERFERER_1", "DESTROY_INTERFERER_2",
                    "DESTROY_INTERFERER_3", "DESTROY_INTERFERER_4");

                T.SetSegue(Segue.Hostile);
            }
            else if (GetGameState("KNOW_UMGAH_ZOMBIES") != 0)
            {
                numVisits = Greeting(numVisits, "REVEALED_ZOMBIE_HELLO_1", "REVEALED_ZOMBIE_HELLO_2",
                    "REVEALED_ZOMBIE_HELLO_3", "REVEALED_ZOMBIE_HELLO_4");

                T.SetSegue(Segue.Hostile);
            }
            else
            {
                numVisits = Greeting(numVisits, "UNKNOWN_ZOMBIE_HELLO_1", "UNKNOWN_ZOMBIE_HELLO_2",
                    "UNKNOWN_ZOMBIE_HELLO_3", "UNKNOWN_ZOMBIE_HELLO_4");

                Zombies(0);
            }
            // After the replies are offered, as umgahc.c writes it.
            SetGameState("UMGAH_VISITS", numVisits);
        }
        else if (GetGameState("TALKING_PET") == 0)
        {
            if (AtHome)
            {
                numVisits = Greeting(GetGameState("UMGAH_HOME_VISITS"), "HWLD_PRE_ZOMBIE_HELLO_1",
                    "HWLD_PRE_ZOMBIE_HELLO_2", "HWLD_PRE_ZOMBIE_HELLO_3", "HWLD_PRE_ZOMBIE_HELLO_4");
                SetGameState("UMGAH_HOME_VISITS", numVisits);
            }
            else
            {
                numVisits = Greeting(GetGameState("UMGAH_VISITS"), "SPACE_PRE_ZOMBIE_HELLO_1",
                    "SPACE_PRE_ZOMBIE_HELLO_2", "SPACE_PRE_ZOMBIE_HELLO_3", "SPACE_PRE_ZOMBIE_HELLO_4");
                SetGameState("UMGAH_VISITS", numVisits);
            }

            NormalUmgah(0);
        }
        else
        {
            if (AtHome)
            {
                NPCPhrase("POST_ZOMBIE_HWLD_HELLO");

                UmgahReward(0);
            }
            else
            {
                numVisits = Greeting(GetGameState("UMGAH_VISITS"), "REWARD_AT_HOMEWORLD_1", "REWARD_AT_HOMEWORLD_2");
                SetGameState("UMGAH_VISITS", numVisits);

                T.SetSegue(Segue.Peace);
            }
        }
    }

    protected internal override void PostEncounter()
    {
        if (GetGameState("UMGAH_ZOMBIE_BLOBBIES") == 0)
            SetGameState("MET_NORMAL_UMGAH", 1);
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        if ((GetGameState("TALKING_PET") != 0 && GetGameState("UMGAH_HOSTILE") == 0)
            || T.Activity == TalkActivity.WonLastBattle)
            T.SetSegue(Segue.Peace);
        else
            T.SetSegue(Segue.Hostile);
    }
}
