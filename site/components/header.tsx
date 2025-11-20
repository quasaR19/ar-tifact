import { AuthButton } from "@/components/auth-button";
import { ThemeSwitcher } from "@/components/theme-switcher";
import Link from "next/link";

export function Header() {
  return (
    <header className="w-full border-b border-b-foreground/10">
      <nav className="w-full max-w-7xl mx-auto flex justify-between items-center p-3 px-5 h-16">
        <div className="flex gap-5 items-center font-semibold">
          <Link href="/">AR-tifact</Link>
        </div>
        <div className="flex items-center gap-4">
          <ThemeSwitcher />
          <AuthButton />
        </div>
      </nav>
    </header>
  );
}
