import Reveal from "reveal.js";
import RevealNotes from "reveal.js/plugin/notes";
import RevealSearch from "reveal.js/plugin/search";
import RevealZoom from "reveal.js/plugin/zoom";
import "reveal.js/reset.css";
import "reveal.js/reveal.css";
import "./theme.css";

const slidesContainer = document.querySelector(".slides");
const slidesUrl = `${import.meta.env.BASE_URL}slides.html`;
const response = await fetch(slidesUrl);

if (!response.ok) {
  throw new Error(`Unable to load slides from ${slidesUrl}: ${response.status}`);
}

slidesContainer.innerHTML = await response.text();

const demoAppUrl = new URL(
  import.meta.env.VITE_DEMO_APP_URL || "http://localhost:4200",
);

for (const link of document.querySelectorAll("[data-demo-path]")) {
  link.href = new URL(link.dataset.demoPath, demoAppUrl).href;
}

const deck = new Reveal({
  controls: true,
  controlsTutorial: false,
  hash: true,
  history: true,
  navigationMode: "linear",
  progress: true,
  slideNumber: "c/t",
  transition: "fade",
  backgroundTransition: "fade",
  width: 1280,
  height: 720,
  margin: 0.055,
  plugins: [RevealNotes, RevealSearch, RevealZoom],
});

await deck.initialize();

window.revealDeck = deck;
document.documentElement.classList.add("deck-ready");
