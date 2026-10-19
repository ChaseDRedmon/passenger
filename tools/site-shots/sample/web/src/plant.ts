/** A plant on a bench, as the greenhouse's data names it. */
export interface Plant {
  name: string;
  bench: "North" | "South" | "Shade";
  capacityLitres: number;
  dailyUseLitres: number;
  /** How wet its pot is now: 0 dry, 1 full. */
  moisture: number;
}

/** Whole days from a full pot to a dry one, never less than one. */
export function daysBetweenWatering(plant: Plant): number {
  return Math.max(1, Math.floor(plant.capacityLitres / plant.dailyUseLitres));
}

export function dueOn(plants: readonly Plant[], day: number): Plant[] {
  return plants.filter((plant) => day % daysBetweenWatering(plant) === 0);
}
