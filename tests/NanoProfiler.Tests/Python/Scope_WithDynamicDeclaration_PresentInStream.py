import helpers

def verify(tp):
    helpers.verify_slice_count_prefix(tp, "DynamicScope_", 5)

if __name__ == "__main__":
    helpers.main(verify)
