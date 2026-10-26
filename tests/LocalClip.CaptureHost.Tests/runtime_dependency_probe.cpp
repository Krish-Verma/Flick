// Deliberately dynamically linked to winpthread in CI. This is a negative
// packaging fixture, never a shipped component or a recording test.
#include <pthread.h>

int main() {
    return pthread_equal(pthread_self(), pthread_self()) ? 0 : 1;
}
