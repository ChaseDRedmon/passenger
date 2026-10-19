import plants from "../data/plants.json";
import { type Plant, dueOn } from "./plant";

/** One row of the page: a plant's name, its bench and a bar as wet as its pot. */
function row(plant: Plant): HTMLElement {
  const item = document.createElement("li");
  item.className = "plant";
  item.innerHTML = `<strong>${plant.name}</strong><span>${plant.bench} bench</span>`;
  const bar = document.createElement("meter");
  bar.value = plant.moisture;
  bar.low = 0.3;
  item.append(bar);
  return item;
}

export function render(list: HTMLElement, day: number): void {
  const all = plants as Plant[];
  const due = dueOn(all, day);
  list.replaceChildren(...all.map(row));
  const heading = document.querySelector("h1");
  if (heading) {
    heading.textContent = `Day ${day}: ${due.length} plants to water`;
  }
}
