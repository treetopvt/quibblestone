// ----------------------------------------------------------------------------
//  PurchaserSession - the app-wide, IN-MEMORY holder of the signed-in purchaser
//  credential (accounts-identity/03, issue #69).
//
//  WHY THIS EXISTS: the purchaser credential used to live in Account.tsx's local
//  useState, so it was discarded the moment the purchaser navigated away from the
//  Account screen - meaning a return to Account (or the keepsake cloud gallery)
//  forced a fresh sign-in every time within one session. Lifting it into a
//  context provider mounted ABOVE <Routes> (see main.tsx) lets sign-in persist
//  across client-side navigation for the SPA's lifetime, so the purchaser signs
//  in once per visit.
//
//  DELIBERATELY IN-MEMORY ONLY (no localStorage / sessionStorage): the credential
//  is a short-lived bearer, and NOT persisting it keeps it from lingering on a
//  shared or child's device across reloads / tab closes (README section 6,
//  child-safety posture). So the session survives client-side navigation but is
//  gone on a full page reload or a new tab - an accepted trade-off; a durable,
//  persisted sign-in is a separate decision, not this.
//
//  AUTH BOUNDARY (accounts-identity/03, NON-NEGOTIABLE): this is consumed ONLY by
//  purchaser-facing surfaces (the Account screen and, through it, the keepsake
//  cloud gallery + the billing restore list). It is NEVER read by the join-code /
//  lobby / word-entry / reveal flow or the SignalR hook - free play never depends
//  on sign-in state. The provider holding the value app-wide does not change that:
//  only purchaser surfaces call usePurchaserSession.
//
//  Prose: hyphens / colons / parentheses, never em dashes.
// ----------------------------------------------------------------------------

import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';

/** The signed-in purchaser session, held in memory for the SPA's lifetime. */
export interface PurchaserSession {
  /** The short-lived purchaser bearer credential, or null when signed out. Never persisted. */
  credential: string | null;
  /** The signed-in purchaser email (for the "signed in as X" UI), or null when signed out. */
  email: string | null;
  /** True when a purchaser credential is currently held. */
  isSignedIn: boolean;
  /** Record a completed sign-in (called from the AccountsController verify result). */
  signIn: (credential: string, email: string) => void;
  /** Forget the current sign-in. In-memory state clears on reload regardless; this is the explicit sign-out. */
  signOut: () => void;
}

const PurchaserSessionContext = createContext<PurchaserSession | null>(null);

/**
 * Provides the in-memory purchaser session to the tree. Mount it ABOVE the router
 * (main.tsx) so the credential survives client-side navigation between screens.
 */
export function PurchaserSessionProvider({ children }: { children: ReactNode }) {
  const [credential, setCredential] = useState<string | null>(null);
  const [email, setEmail] = useState<string | null>(null);

  const signIn = useCallback((nextCredential: string, nextEmail: string) => {
    setCredential(nextCredential);
    setEmail(nextEmail);
  }, []);

  const signOut = useCallback(() => {
    setCredential(null);
    setEmail(null);
  }, []);

  const value = useMemo<PurchaserSession>(
    () => ({ credential, email, isSignedIn: credential !== null, signIn, signOut }),
    [credential, email, signIn, signOut],
  );

  return <PurchaserSessionContext.Provider value={value}>{children}</PurchaserSessionContext.Provider>;
}

/**
 * Reads the purchaser session. Throws if used outside {@link PurchaserSessionProvider},
 * so a miswired consumer fails loudly in dev rather than silently seeing "signed out".
 */
export function usePurchaserSession(): PurchaserSession {
  const context = useContext(PurchaserSessionContext);
  if (context === null) {
    throw new Error('usePurchaserSession must be used within a PurchaserSessionProvider');
  }
  return context;
}
