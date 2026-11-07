/* Stand-in for a NativeAOT-LLVM guest: a wasi-libc reactor that uses the same
 * libc services as the .NET runtime libraries (getenv, clock_gettime,
 * arc4random_buf, write, a constructor), plus the morphir_bytes_v1 imports. */
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

__attribute__((import_module("morphir_bytes_v1"), import_name("input_len"))) int input_len(void);
__attribute__((import_module("morphir_bytes_v1"), import_name("input_byte"))) int input_byte(int);
__attribute__((import_module("morphir_bytes_v1"), import_name("output_byte"))) void output_byte(int);

static int initialized;
__attribute__((constructor)) static void init(void) {
  struct timespec ts; unsigned char seed[8];
  clock_gettime(CLOCK_MONOTONIC, &ts);
  arc4random_buf(seed, sizeof seed);
  if (getenv("DOTNET_gcServer")) write(2, "x", 1);
  initialized = 1;
}

__attribute__((export_name("handle"))) int handle(void) {
  if (!initialized) return -1; /* the runtime was not initialized */
  int n = input_len();
  for (int i = 0; i < n; i++) output_byte(input_byte(i));
  return n;
}
