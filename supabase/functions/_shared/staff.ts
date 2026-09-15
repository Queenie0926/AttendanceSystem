export interface NamedStaff {
  first_name: string;
  middle_name: string | null;
  last_name: string;
}

export function formatFullName(staff: NamedStaff): string {
  return [staff.first_name, staff.middle_name, staff.last_name]
    .filter((part) => !!part && part.trim().length > 0)
    .join(" ");
}
