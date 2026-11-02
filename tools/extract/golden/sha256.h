/*
 * Attack on Crateris: SHA-256 (FIPS 180-4) for the golden recorders, so
 * that large maps and frames leave the program as hashes rather than hex.
 *
 * Copyright (C) 2026 Attack on Crateris contributors.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 */

#ifndef CRATERIS_SHA256_H
#define CRATERIS_SHA256_H

#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

typedef struct
{
	uint32_t h[8];
	uint64_t length;            /* bytes so far */
	unsigned char block[64];
	size_t used;                /* bytes in block */
} Sha256;

static const uint32_t sha256_k[64] = {
	0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1,
	0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
	0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786,
	0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
	0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147,
	0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
	0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b,
	0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
	0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a,
	0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
	0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
};

#define SHA256_ROR(x, n) (((x) >> (n)) | ((x) << (32 - (n))))

static void
sha256_init (Sha256 *s)
{
	static const uint32_t h0[8] = { 0x6a09e667, 0xbb67ae85, 0x3c6ef372,
			0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19 };
	memcpy (s->h, h0, sizeof h0);
	s->length = 0;
	s->used = 0;
}

static void
sha256_block (Sha256 *s, const unsigned char *p)
{
	uint32_t w[64], a, b, c, d, e, f, g, h;
	int i;
	for (i = 0; i < 16; ++i)
		w[i] = ((uint32_t) p[4 * i] << 24) | ((uint32_t) p[4 * i + 1] << 16)
				| ((uint32_t) p[4 * i + 2] << 8) | p[4 * i + 3];
	for (i = 16; i < 64; ++i)
	{
		uint32_t s0 = SHA256_ROR (w[i - 15], 7) ^ SHA256_ROR (w[i - 15], 18)
				^ (w[i - 15] >> 3);
		uint32_t s1 = SHA256_ROR (w[i - 2], 17) ^ SHA256_ROR (w[i - 2], 19)
				^ (w[i - 2] >> 10);
		w[i] = w[i - 16] + s0 + w[i - 7] + s1;
	}
	a = s->h[0]; b = s->h[1]; c = s->h[2]; d = s->h[3];
	e = s->h[4]; f = s->h[5]; g = s->h[6]; h = s->h[7];
	for (i = 0; i < 64; ++i)
	{
		uint32_t t1 = h + (SHA256_ROR (e, 6) ^ SHA256_ROR (e, 11)
				^ SHA256_ROR (e, 25)) + ((e & f) ^ (~e & g)) + sha256_k[i] + w[i];
		uint32_t t2 = (SHA256_ROR (a, 2) ^ SHA256_ROR (a, 13)
				^ SHA256_ROR (a, 22)) + ((a & b) ^ (a & c) ^ (b & c));
		h = g; g = f; f = e; e = d + t1;
		d = c; c = b; b = a; a = t1 + t2;
	}
	s->h[0] += a; s->h[1] += b; s->h[2] += c; s->h[3] += d;
	s->h[4] += e; s->h[5] += f; s->h[6] += g; s->h[7] += h;
}

static void
sha256_add (Sha256 *s, const void *data, size_t n)
{
	const unsigned char *p = (const unsigned char *) data;
	s->length += n;
	while (n > 0)
	{
		size_t take = 64 - s->used;
		if (take > n)
			take = n;
		memcpy (s->block + s->used, p, take);
		s->used += take;
		p += take;
		n -= take;
		if (s->used == 64)
		{
			sha256_block (s, s->block);
			s->used = 0;
		}
	}
}

static void
sha256_byte (Sha256 *s, unsigned char b)
{
	sha256_add (s, &b, 1);
}

/* The digest as 64 lower-case hex digits and a NUL. */
static void
sha256_hex (Sha256 *s, char out[65])
{
	uint64_t bits = s->length * 8;
	unsigned char tail[8];
	int i;
	sha256_byte (s, 0x80);
	while (s->used != 56)
		sha256_byte (s, 0);
	for (i = 0; i < 8; ++i)
		tail[i] = (unsigned char) (bits >> (56 - 8 * i));
	sha256_add (s, tail, 8);
	for (i = 0; i < 8; ++i)
		sprintf (out + 8 * i, "%08x", (unsigned) s->h[i]);
}

#endif
