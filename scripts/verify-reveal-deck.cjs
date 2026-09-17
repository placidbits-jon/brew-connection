#!/usr/bin/env node
const { chromium } = require(
  "../src/coffee-community-web/node_modules/@playwright/test",
);
const assert = require("node:assert/strict");

const slidesUrl = process.env.SLIDES_URL || "http://localhost:4173";
const expectedDemoOrigin = process.env.DEMO_WEB_URL || "http://localhost:4200";

(async () => {
  const executablePath = process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH;
  const browser = await chromium.launch({
    headless: true,
    ...(executablePath ? { executablePath } : {}),
  });
  try {
    const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
    const errors = [];
    page.on("pageerror", (error) => errors.push(error.message));
    page.on("response", (response) => {
      if (response.status() >= 400) errors.push(`${response.status()} ${response.url()}`);
    });
    page.on("console", (message) => {
      if (message.type() === "error" && !message.text().startsWith("Failed to load resource:")) {
        errors.push(message.text());
      }
    });

    await page.goto(slidesUrl, { waitUntil: "networkidle" });
    await page.locator("html.deck-ready").waitFor();
    await page.waitForFunction(() => window.revealDeck?.isReady());

    const slideCount = await page.locator(".slides > section").count();
    assert.equal(slideCount, 32, "Reveal.js deck should contain all 32 slides");

    const results = [];
    for (let index = 0; index < slideCount; index += 1) {
      await page.evaluate((slideIndex) => window.revealDeck.slide(slideIndex), index);
      const result = await page.locator(".slides > section.present").evaluate((section) => ({
        id: section.dataset.slideId,
        horizontalOverflow: section.scrollWidth - section.clientWidth,
        verticalOverflow: section.scrollHeight - section.clientHeight,
      }));
      results.push(result);
    }

    assert.equal(new Set(results.map(({ id }) => id)).size, 32, "slide IDs should be unique");
    const overflowing = results.filter(
      ({ horizontalOverflow, verticalOverflow }) => horizontalOverflow > 1 || verticalOverflow > 1,
    );
    assert.deepEqual(overflowing, [], `slides overflow their 1280x720 canvas: ${JSON.stringify(overflowing)}`);

    const notes = page.locator('.slides > section[data-slide-kind="demo"] > aside.notes');
    assert.equal(await notes.count(), 27, "every demo slide should have presenter notes");
    for (let index = 0; index < 27; index += 1) {
      const note = notes.nth(index);
      await assert.doesNotReject(async () => {
        assert.match(await note.innerText(), /Activate demo:/);
        assert.match(await note.innerText(), /Action:/);
        assert.match(await note.innerText(), /Look for:/);
      });
      assert.equal(await note.isVisible(), false, "presenter notes should be hidden from the audience view");
    }

    await page.evaluate(() => window.revealDeck.slide(4));
    const speakerViewPromise = page.waitForEvent("popup");
    await page.evaluate(() => window.revealDeck.getPlugin("notes").open());
    const speakerView = await speakerViewPromise;
    const speakerNotes = speakerView.locator(".speaker-controls-notes .value");
    await speakerNotes.waitFor({ state: "visible" });
    const speakerText = await speakerNotes.innerText();
    assert.match(speakerText, /Activate demo:/);
    assert.match(speakerText, /Action:/);
    assert.match(speakerText, /Look for:/);
    await speakerView.close();

    const demoLinks = await page.locator("a[data-demo-path]").evaluateAll((links) =>
      links.map((link) => ({ href: link.href, path: link.dataset.demoPath })),
    );
    assert.equal(demoLinks.length, 27, "deck should contain all 27 demo links");
    for (const link of demoLinks) {
      const expected = new URL(link.path, expectedDemoOrigin).href;
      assert.equal(link.href, expected, `demo link should target the Aspire web resource: ${link.path}`);
    }
    assert.deepEqual(errors, [], `browser errors: ${JSON.stringify(errors)}`);

    console.log(`PASS: ${slideCount} Reveal.js slides fit; 27 presenter notes are hidden from the audience and render in speaker view; ${demoLinks.length} demo links target ${expectedDemoOrigin}.`);
  } finally {
    await browser.close();
  }
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
