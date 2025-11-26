import { SignUpForm } from "@/components/sign-up-form";

export default function Page() {
  return (
    <div className="flex h-[calc(100svh-4rem-1px)] w-full items-center justify-center p-6 md:p-10 overflow-hidden">
      <div className="w-full max-w-sm">
        <SignUpForm />
      </div>
    </div>
  );
}
