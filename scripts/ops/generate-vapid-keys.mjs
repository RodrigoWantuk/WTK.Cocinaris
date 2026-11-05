#!/usr/bin/env node
// Prints a new VAPID key pair for web push (ADR-0013), as the lines api.env takes. The private key
// is a secret: put it in the environment file, never in the repository or a chat. Replacing the
// keys ends every existing subscription, so people must turn notifications on again.
import { generateKeyPairSync } from "node:crypto";

const { publicKey, privateKey } = generateKeyPairSync("ec", { namedCurve: "prime256v1" });
const pub = publicKey.export({ format: "jwk" });
const priv = privateKey.export({ format: "jwk" });
const raw = Buffer.concat([
  Buffer.from([0x04]),
  Buffer.from(pub.x, "base64url"),
  Buffer.from(pub.y, "base64url"),
]);

console.log(`Planning__WebPush__PublicKey=${raw.toString("base64url")}`);
console.log(`Planning__WebPush__PrivateKey=${priv.d}`);
console.log("Planning__WebPush__Subject=mailto:<contact address>");
