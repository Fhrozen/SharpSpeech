# ASR/TTS Circular Test Corpus

> Used by `AsrCircularTests` (tests/FastTTSR.Api.IntegrationTests). These tests are opt-in only —
> see `AsrModelTestGate` — because they generate real audio via the Supertonic-3 TTS model and
> feed it into the Whisper/Nemotron ASR engines to sanity-check real end-to-end transcription,
> downloading real models in the process.
>
> Format: each `## <id> (type: <short|long|conversation>[, speaker: <id>])` heading starts a new
> sample. `short`/`long` samples are a single block of text spoken by one Supertonic-3 speaker
> preset (F1-F5/M1-M5). `conversation` samples are made of `[SPEAKER] text` turn lines, alternating
> speakers - used for the long "streaming" test, which stresses Nemotron's cache-aware
> chunk-to-chunk decoding across many consecutive chunks instead of just one or two.

## short-1 (type: short, speaker: F1)
Good morning! I hope you slept well. The coffee is ready whenever you would like some.

## short-2 (type: short, speaker: M2)
The quarterly report is due this Friday. Please double check all the figures before you submit it. Let me know if you need any help finishing it on time.

## short-3 (type: short, speaker: F3)
Traffic was terrible again this morning. I left the house an hour early and I still barely made it on time. I think it might be worth trying a different route tomorrow.

## long-1 (type: long, speaker: M1)
The history of the small coastal town began several centuries ago, when a group of fishermen first settled along the rocky shoreline. Over time, the settlement grew into a bustling port, attracting traders from distant regions who came to exchange goods and stories. The harbor became known for its sturdy wooden ships and the skilled craftsmen who built them, and the local market filled with spices, cloth, and tools brought in from across the sea. As the decades passed, the town adapted to changing times, welcoming new industries while holding on to its maritime traditions. Today, visitors can still walk along the old stone docks and imagine the ships that once crowded the harbor, even as modern buildings rise just a short distance away.

## long-2 (type: long, speaker: F4)
Learning to cook well takes patience more than natural talent. Most people who become confident in the kitchen started out burning toast or forgetting to season their food, and they only improved because they kept trying new recipes and paying attention to what went wrong. A good approach is to master a handful of simple dishes first, understanding exactly why each step matters, before attempting anything more complicated. Tasting as you go, adjusting seasoning gradually, and not being afraid to make mistakes are all part of the process. With enough practice, the instincts needed to fix a dish on the fly, or to improvise when an ingredient is missing, tend to develop naturally over time.

## conversation-1 (type: conversation)
[F1] Hey, did you get a chance to look at the proposal I sent over yesterday?
[M2] Yeah, I read through most of it last night. I think the budget section needs some work.
[F1] Really? I thought the numbers were pretty solid. What part were you looking at?
[M2] Mostly the marketing line items. It feels like we're underestimating how much the campaign will cost.
[F1] That's fair. I based those numbers on last year's campaign, but prices have gone up since then.
[M2] Right, and we're also planning a bigger push this time, with the new social media channels.
[F1] Good point. Maybe we should ask the marketing team for updated estimates before we finalize anything.
[M2] I can reach out to them this afternoon if you want.
[F1] That would be great, thanks. Did anything else stand out to you?
[M2] The timeline looks tight, especially for the design phase. Are we sure two weeks is enough?
[F1] Honestly, I was worried about that too. The design team has been swamped lately.
[M2] Maybe we push the launch date back by a week, just to give everyone some breathing room.
[F1] That could work, as long as it doesn't conflict with the conference in October.
[M2] Let me check the conference dates and get back to you.
[F1] Sounds good. In the meantime, I'll update the budget numbers once marketing gets back to us.
[M2] Perfect. Should we schedule a follow-up meeting for later this week?
[F1] Yes, let's say Thursday afternoon, once we have the new estimates in hand.
[M2] Works for me. I'll send a calendar invite once I confirm the conference dates.
[F1] Great, thanks for going through this so carefully. I think the plan will be much stronger for it.
[M2] No problem, happy to help. This project has a lot of potential if we get the details right.
